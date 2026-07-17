using System.Runtime.InteropServices;
using Switcher.Contracts;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using Compiler = Vortice.D3DCompiler.Compiler;

namespace Switcher.Media.Compositing;

/// <summary>
/// Composites decoded frames as GPU textures using DirectX 11 (docs/specs/pc-switcher-app.md §2.2):
/// each visible layer is drawn as an alpha-blended, cropped textured quad, back-to-front by Z-order,
/// into an offscreen render target, which is then read back into a <see cref="FrameData"/> for the
/// virtual camera / UI. Requires a Direct3D 11 runtime (Windows); see README.md.
/// </summary>
internal sealed class DirectX11Compositor : IGpuCompositor
{
    // BGRA to match the raw output caps negotiated by Switcher.Media.GStreamer.PipelineDescriptorFactory.
    private const Format PixelFormat = Format.B8G8R8A8_UNorm;
    private const int BytesPerPixel = 4;

    private static readonly string VertexShaderSource = """
        cbuffer Transform : register(b0)
        {
            float2 destScale;
            float2 destOffset;
            float4 cropRect;   // minU, minV, maxU, maxV
            float opacity;
            float3 _padding;
        };

        struct VSIn { float2 pos : POSITION; float2 uv : TEXCOORD0; };
        struct VSOut { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

        VSOut main(VSIn input)
        {
            VSOut o;
            o.pos = float4(input.pos * destScale + destOffset, 0, 1);
            o.uv = lerp(cropRect.xy, cropRect.zw, input.uv);
            return o;
        }
        """;

    private static readonly string PixelShaderSource = """
        cbuffer Transform : register(b0)
        {
            float2 destScale;
            float2 destOffset;
            float4 cropRect;
            float opacity;
            float3 _padding;
        };

        Texture2D tex : register(t0);
        SamplerState samp : register(s0);

        float4 main(float4 pos : SV_POSITION, float2 uv : TEXCOORD0) : SV_TARGET
        {
            float4 c = tex.Sample(samp, uv);
            c.a *= opacity;
            return c;
        }
        """;

    // Triangle-strip unit quad: (x, y, u, v). D3D texture V=0 is the top row.
    private static readonly float[] QuadVertices =
    [
        -1f, 1f, 0f, 0f,
        -1f, -1f, 0f, 1f,
        1f, 1f, 1f, 0f,
        1f, -1f, 1f, 1f,
    ];

    private readonly object _lock = new();
    private readonly Dictionary<int, SourceTexture> _sourceTextures = new();

    private ID3D11Device? _device;
    private ID3D11DeviceContext? _context;
    private ID3D11VertexShader? _vertexShader;
    private ID3D11PixelShader? _pixelShader;
    private ID3D11InputLayout? _inputLayout;
    private ID3D11Buffer? _vertexBuffer;
    private ID3D11Buffer? _transformBuffer;
    private ID3D11SamplerState? _sampler;
    private ID3D11BlendState? _blendState;

    private ID3D11Texture2D? _renderTargetTexture;
    private ID3D11RenderTargetView? _renderTargetView;
    private ID3D11Texture2D? _stagingTexture;
    private int _canvasWidth;
    private int _canvasHeight;

    private bool _disposed;

    public FrameData Compose(IReadOnlyList<CompositedLayer> layers, Func<int, FrameData?> frameLookup, int canvasWidth, int canvasHeight)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(canvasWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(canvasHeight);

        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            EnsureDevice();
            EnsureCanvas(canvasWidth, canvasHeight);

            _context!.ClearRenderTargetView(_renderTargetView, new Color4(0f, 0f, 0f, 1f));
            _context.OMSetRenderTargets(_renderTargetView!);
            _context.OMSetBlendState(_blendState);
            _context.RSSetViewport(new Viewport(0, 0, canvasWidth, canvasHeight));
            _context.IASetPrimitiveTopology(PrimitiveTopology.TriangleStrip);
            _context.IASetInputLayout(_inputLayout);
            _context.IASetVertexBuffer(0, _vertexBuffer!, sizeof(float) * 4);
            _context.VSSetShader(_vertexShader);
            _context.PSSetShader(_pixelShader);
            _context.PSSetSampler(0, _sampler);
            _context.VSSetConstantBuffer(0, _transformBuffer);
            _context.PSSetConstantBuffer(0, _transformBuffer);

            var seenChannels = new HashSet<int>();
            foreach (var layer in layers)
            {
                var frame = frameLookup(layer.Channel);
                if (frame is null || frame.Width <= 0 || frame.Height <= 0)
                {
                    continue;
                }

                seenChannels.Add(layer.Channel);
                var texture = GetOrCreateSourceTexture(layer.Channel, frame.Width, frame.Height);
                UploadFrame(texture, frame);

                UpdateTransformBuffer(layer.Settings, frame, canvasWidth, canvasHeight);
                _context.PSSetShaderResource(0, texture.View);
                _context.Draw(4, 0);
            }

            EvictUnusedSourceTextures(seenChannels);

            return ReadBackCanvas(canvasWidth, canvasHeight);
        }
    }

    private void EnsureDevice()
    {
        if (_device is not null)
        {
            return;
        }

        FeatureLevel[] featureLevels = [FeatureLevel.Level_11_0];
        var result = D3D11.D3D11CreateDevice(
            null,
            Vortice.Direct3D.DriverType.Hardware,
            DeviceCreationFlags.BgraSupport,
            featureLevels,
            out ID3D11Device? device,
            out _,
            out ID3D11DeviceContext? context);

        if (result.Failure || device is null || context is null)
        {
            result = D3D11.D3D11CreateDevice(
                null,
                Vortice.Direct3D.DriverType.Warp,
                DeviceCreationFlags.BgraSupport,
                featureLevels,
                out device,
                out _,
                out context);
            result.CheckError();
        }

        _device = device;
        _context = context;

        var vsBlob = Compiler.Compile(VertexShaderSource, "main", "compositor_vs", "vs_4_0");
        var psBlob = Compiler.Compile(PixelShaderSource, "main", "compositor_ps", "ps_4_0");

        _vertexShader = _device.CreateVertexShader(vsBlob.Span);
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

        var transformBufferDescription = new BufferDescription
        {
            Usage = ResourceUsage.Dynamic,
            ByteWidth = TransformBufferSizeBytes,
            BindFlags = BindFlags.ConstantBuffer,
            CPUAccessFlags = CpuAccessFlags.Write,
        };
        _transformBuffer = _device.CreateBuffer(transformBufferDescription);

        _sampler = _device.CreateSamplerState(SamplerDescription.LinearClamp);
        _blendState = _device.CreateBlendState(new BlendDescription(Blend.SourceAlpha, Blend.InverseSourceAlpha));
    }

    // float2 destScale, float2 destOffset, float4 cropRect, float opacity, float3 padding = 12 floats.
    private const uint TransformBufferSizeBytes = 12 * sizeof(float);

    private void EnsureCanvas(int width, int height)
    {
        if (_renderTargetTexture is not null && _canvasWidth == width && _canvasHeight == height)
        {
            return;
        }

        _renderTargetView?.Dispose();
        _renderTargetTexture?.Dispose();
        _stagingTexture?.Dispose();

        var renderTargetDescription = new Texture2DDescription
        {
            Width = (uint)width,
            Height = (uint)height,
            MipLevels = 1,
            ArraySize = 1,
            Format = PixelFormat,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource,
        };
        _renderTargetTexture = _device!.CreateTexture2D(renderTargetDescription);
        _renderTargetView = _device.CreateRenderTargetView(_renderTargetTexture);

        var stagingDescription = renderTargetDescription with
        {
            Usage = ResourceUsage.Staging,
            BindFlags = BindFlags.None,
            CPUAccessFlags = CpuAccessFlags.Read,
        };
        _stagingTexture = _device.CreateTexture2D(stagingDescription);

        _canvasWidth = width;
        _canvasHeight = height;
    }

    private SourceTexture GetOrCreateSourceTexture(int channel, int width, int height)
    {
        if (_sourceTextures.TryGetValue(channel, out var existing) && existing.Width == width && existing.Height == height)
        {
            return existing;
        }

        existing?.Dispose();

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
        var texture = _device!.CreateTexture2D(description);
        var view = _device.CreateShaderResourceView(texture);
        var created = new SourceTexture(texture, view, width, height);
        _sourceTextures[channel] = created;
        return created;
    }

    private void EvictUnusedSourceTextures(HashSet<int> seenChannels)
    {
        foreach (var channel in _sourceTextures.Keys.Except(seenChannels).ToList())
        {
            _sourceTextures[channel].Dispose();
            _sourceTextures.Remove(channel);
        }
    }

    private unsafe void UploadFrame(SourceTexture texture, FrameData frame)
    {
        var mapped = _context!.Map(texture.Texture, 0, MapMode.WriteDiscard, Vortice.Direct3D11.MapFlags.None);
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
            _context.Unmap(texture.Texture, 0);
        }
    }

    private unsafe void UpdateTransformBuffer(PipSettings settings, FrameData frame, int canvasWidth, int canvasHeight)
    {
        var scaleX = settings.Width / (float)canvasWidth;
        var scaleY = settings.Height / (float)canvasHeight;
        var offsetX = (settings.X + settings.Width / 2f) / canvasWidth * 2f - 1f;
        var offsetY = 1f - (settings.Y + settings.Height / 2f) / canvasHeight * 2f;

        var crop = settings.Crop;
        var minU = 0f;
        var minV = 0f;
        var maxU = 1f;
        var maxV = 1f;
        if (crop is not null)
        {
            minU = Math.Clamp(crop.Left / (float)frame.Width, 0f, 1f);
            minV = Math.Clamp(crop.Top / (float)frame.Height, 0f, 1f);
            maxU = Math.Clamp(crop.Right / (float)frame.Width, 0f, 1f);
            maxV = Math.Clamp(crop.Bottom / (float)frame.Height, 0f, 1f);
        }

        float[] values =
        [
            scaleX, scaleY,
            offsetX, offsetY,
            minU, minV, maxU, maxV,
            (float)settings.Opacity,
            0f, 0f, 0f,
        ];

        var mapped = _context!.Map(_transformBuffer!, 0, MapMode.WriteDiscard, Vortice.Direct3D11.MapFlags.None);
        try
        {
            Marshal.Copy(values, 0, mapped.DataPointer, values.Length);
        }
        finally
        {
            _context.Unmap(_transformBuffer, 0);
        }
    }

    private FrameData ReadBackCanvas(int width, int height)
    {
        _context!.CopyResource(_stagingTexture!, _renderTargetTexture!);
        var mapped = _context.Map(_stagingTexture!, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
        try
        {
            var rowBytes = width * BytesPerPixel;
            var pixels = new byte[rowBytes * height];
            unsafe
            {
                var source = (byte*)mapped.DataPointer;
                for (var row = 0; row < height; row++)
                {
                    var sourceRow = new ReadOnlySpan<byte>(source + (nint)row * mapped.RowPitch, rowBytes);
                    sourceRow.CopyTo(pixels.AsSpan(row * rowBytes, rowBytes));
                }
            }

            return new FrameData(width, height, pixels);
        }
        finally
        {
            _context.Unmap(_stagingTexture, 0);
        }
    }

    private sealed record SourceTexture(ID3D11Texture2D Texture, ID3D11ShaderResourceView View, int Width, int Height) : IDisposable
    {
        public void Dispose()
        {
            View.Dispose();
            Texture.Dispose();
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

            foreach (var texture in _sourceTextures.Values)
            {
                texture.Dispose();
            }

            _sourceTextures.Clear();

            _renderTargetView?.Dispose();
            _renderTargetTexture?.Dispose();
            _stagingTexture?.Dispose();
            _blendState?.Dispose();
            _sampler?.Dispose();
            _transformBuffer?.Dispose();
            _vertexBuffer?.Dispose();
            _inputLayout?.Dispose();
            _pixelShader?.Dispose();
            _vertexShader?.Dispose();
            _context?.Dispose();
            _device?.Dispose();

            _disposed = true;
        }
    }
}
