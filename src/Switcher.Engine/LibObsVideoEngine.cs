using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text.Json;
using Switcher.Contracts;

namespace Switcher.Engine;

/// <summary>
/// The production <see cref="IVideoEngine"/>: drives the native <c>switcher-engine</c> (libobs) over
/// P/Invoke. libobs / OBS runtime is Windows-only, so this type is only exercised at runtime on a
/// configured host; the managed assembly itself compiles and unit-tests everywhere (via
/// <see cref="FakeVideoEngine"/>). Source metadata / channel resolution is tracked managed-side (the
/// native layer deals in pixels), while compositing and output routing are forwarded to libobs.
/// </summary>
public sealed class LibObsVideoEngine : IVideoEngine, IDisposable
{
    private static readonly JsonSerializerOptions Json = ProtocolJsonOptions.Default;

    private readonly object _gate = new();
    private readonly Dictionary<string, SourceInfo> _sourcesById = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _channelById = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, FrameData> _lastFrameByTarget = new();

    private IntPtr _ctx;
    private int _nextChannel;
    private IReadOnlyList<OutputAssignment> _assignments = [];

    // Kept alive for the lifetime of the native context so the GC never collects the thunks libobs holds.
    private NativeMethods.FrameCallback? _frameCallback;
    private NativeMethods.StateCallback? _stateCallback;

    public event EventHandler<SourceInfo>? SourceStatusChanged;

    public Task StartAsync(EngineOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        lock (_gate)
        {
            if (_ctx != IntPtr.Zero)
            {
                return Task.CompletedTask;
            }

            _ctx = NativeMethods.engine_startup(JsonSerializer.Serialize(options, Json));
            if (_ctx == IntPtr.Zero)
            {
                throw new InvalidOperationException("Native switcher-engine failed to start (libobs unavailable?).");
            }

            _frameCallback = OnNativeFrame;
            _stateCallback = OnNativeState;
            NativeMethods.engine_set_frame_cb(_ctx, Marshal.GetFunctionPointerForDelegate(_frameCallback), IntPtr.Zero);
            NativeMethods.engine_set_state_cb(_ctx, Marshal.GetFunctionPointerForDelegate(_stateCallback), IntPtr.Zero);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        Dispose();
        return Task.CompletedTask;
    }

    public IReadOnlyList<SourceInfo> GetSources()
    {
        lock (_gate)
        {
            return _sourcesById.Values.OrderBy(s => s.Order ?? s.Channel).ToList();
        }
    }

    public void AddSource(SourceDefinition source)
    {
        ArgumentNullException.ThrowIfNull(source);
        int channel;
        lock (_gate)
        {
            channel = _channelById.TryGetValue(source.Id, out var existing) ? existing : AllocateChannelLocked(source.Id);
            _sourcesById[source.Id] = new SourceInfo(channel, source.Name, ToProtocol(source.Type), null, SourceStatus.Disconnected, source.Id, channel);
        }

        var settings = source.Type switch
        {
            SourceType.Ndi => JsonSerializer.Serialize(source.Ndi, Json),
            SourceType.Webcam => JsonSerializer.Serialize(source.Webcam, Json),
            SourceType.Srt => JsonSerializer.Serialize(source.Srt, Json),
            _ => "{}",
        };

        RequireCtx();
        NativeMethods.engine_add_source(_ctx, source.Id, source.Type.ToString().ToUpperInvariant(), settings);
        SourceStatusChanged?.Invoke(this, _sourcesById[source.Id]);
    }

    public void AddSource(int channel, SourceProtocol protocol, string? sourceUrl)
    {
        var id = $"ch{channel}";
        lock (_gate)
        {
            _channelById[id] = channel;
            if (channel >= _nextChannel)
            {
                _nextChannel = channel + 1;
            }

            _sourcesById[id] = new SourceInfo(channel, id, protocol, null, SourceStatus.Disconnected, id, channel);
        }

        RequireCtx();
        NativeMethods.engine_add_source(_ctx, id, protocol.ToString().ToUpperInvariant(), sourceUrl is null ? "{}" : JsonSerializer.Serialize(new { url = sourceUrl }, Json));
    }

    public void RemoveSource(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        lock (_gate)
        {
            _channelById.Remove(id);
            _sourcesById.Remove(id);
        }

        RequireCtx();
        NativeMethods.engine_remove_source(_ctx, id);
    }

    public bool TryResolveChannel(string id, out int channel)
    {
        lock (_gate)
        {
            return _channelById.TryGetValue(id, out channel);
        }
    }

    public void SetSourceEnabled(ProgramBus bus, string sourceId, bool enabled)
    {
        RequireCtx();
        NativeMethods.engine_set_source_enabled(_ctx, BusToInt(bus), sourceId, enabled);
    }

    public void ApplyProgram(ProgramRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireCtx();
        NativeMethods.engine_apply_program(_ctx, JsonSerializer.Serialize(request, Json));
    }

    public void ApplyPipSettings(int channel, PipSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        string? id;
        lock (_gate)
        {
            id = _channelById.FirstOrDefault(kv => kv.Value == channel).Key;
        }

        if (id is null)
        {
            return;
        }

        RequireCtx();
        NativeMethods.engine_set_pip(_ctx, BusToInt(ProgramBus.Pgm1), id, JsonSerializer.Serialize(settings, Json));
    }

    public void Take()
    {
        RequireCtx();
        NativeMethods.engine_take(_ctx, BusToInt(ProgramBus.Pgm1), 0, 0);
    }

    public FrameData GetFrame(string target) => _lastFrameByTarget.TryGetValue(target, out var f) ? f : Empty;

    public void ApplyMultiview(MultiviewLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        RequireCtx();
        NativeMethods.engine_apply_multiview(_ctx, JsonSerializer.Serialize(layout, Json));
    }

    public void ApplyOutputs(OutputsRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireCtx();
        var rc = NativeMethods.engine_apply_outputs(_ctx, JsonSerializer.Serialize(request, Json));
        if (rc != 0)
        {
            throw new ArgumentException($"Native engine rejected output assignments (code {rc}).", nameof(request));
        }

        lock (_gate)
        {
            _assignments = request.Outputs.ToList();
        }
    }

    public IReadOnlyList<OutputAssignment> CurrentAssignments
    {
        get { lock (_gate) { return _assignments; } }
    }

    public void StartDisplayOutput(string target, IntPtr windowHandle, int displayId)
    {
        RequireCtx();
        NativeMethods.engine_start_display(_ctx, target, windowHandle, displayId);
    }

    public void StopDisplayOutput(string target)
    {
        RequireCtx();
        NativeMethods.engine_stop_display(_ctx, target);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_ctx != IntPtr.Zero)
            {
                NativeMethods.engine_shutdown(_ctx);
                _ctx = IntPtr.Zero;
            }

            _frameCallback = null;
            _stateCallback = null;
        }
    }

    private void OnNativeFrame(IntPtr user, string target, IntPtr bgra, int width, int height, int stride)
    {
        if (bgra == IntPtr.Zero || width <= 0 || height <= 0)
        {
            return;
        }

        var buffer = new byte[height * stride];
        Marshal.Copy(bgra, buffer, 0, buffer.Length);
        _lastFrameByTarget[target] = new FrameData(width, height, buffer);
    }

    private void OnNativeState(IntPtr user, string stateJson)
    {
        // State (per-bus PGM/PVW) is surfaced to the App via the orchestrator's own shadow state today;
        // this hook is where a future native-driven tally/backlight path would deserialize stateJson.
    }

    private void RequireCtx()
    {
        if (_ctx == IntPtr.Zero)
        {
            throw new InvalidOperationException("Video engine has not been started.");
        }
    }

    private int AllocateChannelLocked(string id)
    {
        var channel = _nextChannel++;
        _channelById[id] = channel;
        return channel;
    }

    private static int BusToInt(ProgramBus bus) => bus == ProgramBus.Pgm2 ? 1 : 0;

    private static readonly FrameData Empty = new(0, 0, ReadOnlyMemory<byte>.Empty);

    private static SourceProtocol ToProtocol(SourceType type) => type switch
    {
        SourceType.Ndi => SourceProtocol.Ndi,
        SourceType.Webcam => SourceProtocol.Uvc,
        SourceType.Srt => SourceProtocol.Srt,
        _ => SourceProtocol.Uvc,
    };
}
