using Switcher.Contracts;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using Compiler = Vortice.D3DCompiler.Compiler;

namespace Switcher.VirtualCam.Display;

/// <summary>
/// DXGI/Direct3D 11 <see cref="ISwapChainOutput"/>: stretches each composited <see cref="FrameData"/>
/// (BGRA32) over a full-screen exclusive swap chain on the assigned monitor. Requires a Direct3D 11
/// runtime (Windows); see README.md. Mirrors the upload/quad approach of
/// <c>Switcher.Media.Compositing.DirectX11Compositor</c>, but stretch-to-fit with no blending, since
/// this is a single full-frame present rather than a layered composite.
/// </summary>
internal sealed class Direct3DSwapChainOutput : ISwapChainOutput
{
    private const Format PixelFormat = Format.B8G8R8A8_UNorm;
    private const int BytesPerPixel = 4;
    private const int SwapChainBufferCount = 2;

    private static readonly string VertexShaderSource = """
        struct VSIn { float2 pos : POSITION; float2 uv : TEXCOORD0; };
        struct VSOut { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

        VSOut main(VSIn input)
        {
            VSOut o;
            o.pos = float4(input.pos, 0, 1);
            o.uv = input.uv;
            return o;
        }
        """;

    private static readonly string PixelShaderSource = """
        Texture2D tex : register(t0);
        SamplerState samp : register(s0);

        float4 main(float4 pos : SV_POSITION, float2 uv : TEXCOORD0) : SV_TARGET
        {
            return tex.Sample(samp, uv);
        }
        """;

    // Triangle-strip unit quad covering the whole viewport: (x, y, u, v).
    private static readonly float[] QuadVertices =
    [
        -1f, 1f, 0f, 0f,
        -1f, -1f, 0f, 1f,
        1f, 1f, 1f, 0f,
        1f, -1f, 1f, 1f,
    ];

    private readonly object _lock = new();

    private ID3D11Device? _device;
    private ID3D11DeviceContext? _context;
    private IDXGISwapChain? _swapChain;
    private ID3D11VertexShader? _vertexShader;
    private ID3D11PixelShader? _pixelShader;
    private ID3D11InputLayout? _inputLayout;
    private ID3D11Buffer? _vertexBuffer;
    private ID3D11SamplerState? _sampler;
    private ID3D11RenderTargetView? _renderTargetView;

    private ID3D11Texture2D? _frameTexture;
    private ID3D11ShaderResourceView? _frameTextureView;
    private int _frameTextureWidth;
    private int _frameTextureHeight;

    private int _displayWidth;
    private int _displayHeight;
    private bool _attached;
    private bool _disposed;

    public void AttachToDisplay(int displayIndex, nint windowHandle)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(displayIndex);
        if (windowHandle == 0)
        {
            throw new ArgumentException("A non-zero HWND is required.", nameof(windowHandle));
        }

        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            DetachLocked();

            if (!OperatingSystem.IsWindows())
            {
                throw new PlatformNotSupportedException(
                    "Full-screen physical display output requires Windows (DXGI); see README.md.");
            }

            using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
            factory.EnumAdapters1(0, out var adapter).CheckError();
            using var adapterHandle = adapter;
            adapter.EnumOutputs((uint)displayIndex, out var output).CheckError();
            using var outputHandle = output;

            var bounds = output.Description.DesktopCoordinates;
            _displayWidth = bounds.Right - bounds.Left;
            _displayHeight = bounds.Bottom - bounds.Top;

            var swapChainDescription = new SwapChainDescription
            {
                BufferCount = SwapChainBufferCount,
                BufferDescription = new ModeDescription((uint)_displayWidth, (uint)_displayHeight, PixelFormat),
                SampleDescription = new SampleDescription(1, 0),
                BufferUsage = Usage.RenderTargetOutput,
                OutputWindow = windowHandle,
                Windowed = true,
                SwapEffect = SwapEffect.FlipDiscard,
            };

            // A non-null adapter requires DriverType.Unknown (D3D11CreateDeviceAndSwapChain contract).
            FeatureLevel[] featureLevels = [FeatureLevel.Level_11_0];
            D3D11.D3D11CreateDeviceAndSwapChain(
                adapter,
                DriverType.Unknown,
                DeviceCreationFlags.BgraSupport,
                featureLevels,
                swapChainDescription,
                out _swapChain,
                out _device,
                out _,
                out _context).CheckError();

            // Enter exclusive full screen on the target output, then resize the buffers to match
            // (the standard DXGI windowed-then-fullscreen-then-resize sequence).
            _swapChain!.SetFullscreenState(true, output).CheckError();
            _swapChain.ResizeBuffers((uint)SwapChainBufferCount, (uint)_displayWidth, (uint)_displayHeight, PixelFormat);

            CreatePipelineIfNeeded();
            EnsureRenderTargetView();

            _attached = true;
        }
    }

    public void Present(FrameData frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_attached)
            {
                throw new InvalidOperationException("Present was called before AttachToDisplay.");
            }

            GetOrCreateFrameTexture(frame.Width, frame.Height);
            UploadFrame(frame);

            _context!.ClearRenderTargetView(_renderTargetView, new Color4(0f, 0f, 0f, 1f));
            _context.OMSetRenderTargets(_renderTargetView!);
            _context.RSSetViewport(new Viewport(0, 0, _displayWidth, _displayHeight));
            _context.IASetPrimitiveTopology(PrimitiveTopology.TriangleStrip);
            _context.IASetInputLayout(_inputLayout);
            _context.IASetVertexBuffer(0, _vertexBuffer!, sizeof(float) * 4);
            _context.VSSetShader(_vertexShader);
            _context.PSSetShader(_pixelShader);
            _context.PSSetSampler(0, _sampler);
            _context.PSSetShaderResource(0, _frameTextureView!);
            _context.Draw(4, 0);

            _swapChain!.Present(1, PresentFlags.None);
        }
    }

    public void Detach()
    {
        lock (_lock)
        {
            DetachLocked();
        }
    }

    private void DetachLocked()
    {
        if (_swapChain is not null)
        {
            // Exclusive full-screen swap chains must leave fullscreen before they can be released.
            _swapChain.SetFullscreenState(false, null);
        }

        _frameTextureView?.Dispose();
        _frameTextureView = null;
        _frameTexture?.Dispose();
        _frameTexture = null;
        _frameTextureWidth = 0;
        _frameTextureHeight = 0;

        _renderTargetView?.Dispose();
        _renderTargetView = null;

        _sampler?.Dispose();
        _sampler = null;
        _vertexBuffer?.Dispose();
        _vertexBuffer = null;
        _inputLayout?.Dispose();
        _inputLayout = null;
        _pixelShader?.Dispose();
        _pixelShader = null;
        _vertexShader?.Dispose();
        _vertexShader = null;

        _swapChain?.Dispose();
        _swapChain = null;
        _context?.Dispose();
        _context = null;
        _device?.Dispose();
        _device = null;

        _attached = false;
    }

    private void CreatePipelineIfNeeded()
    {
        if (_vertexShader is not null)
        {
            return;
        }

        var vsBlob = Compiler.Compile(VertexShaderSource, "main", "swapchain_vs", "vs_4_0");
        var psBlob = Compiler.Compile(PixelShaderSource, "main", "swapchain_ps", "ps_4_0");

        _vertexShader = _device!.CreateVertexShader(vsBlob.Span);
        _pixelShader = _device.CreatePixelShader(psBlob.Span);

        InputElementDescription[] inputElements =
        [
            new InputElementDescription("POSITION", 0, Format.R32G32_Float, 0, 0),
            new InputElementDescription("TEXCOORD", 0, Format.R32G32_Float, 8, 0),
        ];
        _inputLayout = _device.CreateInputLayout(inputElements, vsBlob.Span);

        var vertexBufferDescription = new BufferDescription
        {
            Usage = ResourceUsage.Immutable,
            ByteWidth = (uint)(QuadVertices.Length * sizeof(float)),
            BindFlags = BindFlags.VertexBuffer,
        };
        _vertexBuffer = _device.CreateBuffer<float>(QuadVertices.AsSpan(), vertexBufferDescription);

        _sampler = _device.CreateSamplerState(SamplerDescription.LinearClamp);
    }

    private void EnsureRenderTargetView()
    {
        using var backBuffer = _swapChain!.GetBuffer<ID3D11Texture2D>(0);
        _renderTargetView = _device!.CreateRenderTargetView(backBuffer);
    }

    private void GetOrCreateFrameTexture(int width, int height)
    {
        if (_frameTexture is not null && _frameTextureWidth == width && _frameTextureHeight == height)
        {
            return;
        }

        _frameTextureView?.Dispose();
        _frameTexture?.Dispose();

        var description = new Texture2DDescription
        {
            Width = (uint)width,
            Height = (uint)height,
            MipLevels = 1,
            ArraySize = 1,
            Format = PixelFormat,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Dynamic,
            BindFlags = BindFlags.ShaderResource,
            CPUAccessFlags = CpuAccessFlags.Write,
        };
        _frameTexture = _device!.CreateTexture2D(description);
        _frameTextureView = _device.CreateShaderResourceView(_frameTexture);
        _frameTextureWidth = width;
        _frameTextureHeight = height;
    }

    private unsafe void UploadFrame(FrameData frame)
    {
        var mapped = _context!.Map(_frameTexture!, 0, MapMode.WriteDiscard, Vortice.Direct3D11.MapFlags.None);
        try
        {
            var rowBytes = frame.Width * BytesPerPixel;
            var source = frame.Pixels.Span;
            var destination = (byte*)mapped.DataPointer;
            for (var row = 0; row < frame.Height; row++)
            {
                var sourceRow = source.Slice(row * rowBytes, rowBytes);
                var destinationRow = new Span<byte>(destination + (nint)row * mapped.RowPitch, rowBytes);
                sourceRow.CopyTo(destinationRow);
            }
        }
        finally
        {
            _context.Unmap(_frameTexture!, 0);
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            DetachLocked();
            _disposed = true;
        }
    }
}
