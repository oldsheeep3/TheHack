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

    // Fixed readback targets the App's FramePumpService reads every tick; each needs an enabled native tap.
    private static readonly string[] PumpedBusTargets = ["PGM1", "PGM2", "PVW1", "PVW2", "MULTIVIEW"];

    // Lifecycle only (start/dispose). Never held across anything else, so nothing can be waiting on it
    // while the native context is being torn down.
    private readonly object _lifecycleGate = new();

    // Serializes the native calls that return a pointer into an engine-owned buffer, which the next call
    // on the same context overwrites. Deliberately NOT the same lock as anything the libobs graphics
    // thread can touch: these calls can sit inside the native context lock for a second or more (a
    // DirectShow enumeration, the NDI finder's discovery wait), and a graphics thread blocked behind that
    // would deadlock the engine — the native side parks the graphics thread while it holds that same
    // context lock (engine_set_tap, rebind_bus_targets_locked).
    private readonly object _nativeQueryGate = new();

    // Concurrent because the source registry is read and written from the graphics thread (MarkSourceLive,
    // once per tapped frame) as well as from the UI and Web threads. A plain lock here is what created the
    // deadlock above: the graphics thread would block on a lock held by a thread waiting on the native
    // context lock, which in turn was waiting for the graphics thread to park.
    private readonly ConcurrentDictionary<string, SourceInfo> _sourcesById = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, int> _channelById = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, FrameData> _lastFrameByTarget = new();

    private IntPtr _ctx;
    private int _nextChannel = -1;  // Interlocked.Increment hands out 0 first
    private volatile IReadOnlyList<OutputAssignment> _assignments = [];

    // Kept alive for the lifetime of the native context so the GC never collects the thunks libobs holds.
    private NativeMethods.FrameCallback? _frameCallback;
    private NativeMethods.StateCallback? _stateCallback;

    public event EventHandler<SourceInfo>? SourceStatusChanged;

    public event EventHandler<string>? SourceRemoved;

    public Task StartAsync(EngineOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        lock (_lifecycleGate)
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

            // Connect the readback taps for the fixed program/preview/multiview targets the App's frame
            // pump polls via GetFrame(). libobs only delivers frames for a target that has an active tap
            // (engine_set_tap -> video_output_connect); without this no frame ever reaches the UI.
            foreach (var target in PumpedBusTargets)
            {
                NativeMethods.engine_set_tap(_ctx, target, enabled: true);
            }
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        Dispose();
        return Task.CompletedTask;
    }

    public IReadOnlyList<SourceInfo> GetSources() =>
        _sourcesById.Values.OrderBy(s => s.Order ?? s.Channel).ToList();

    public IReadOnlyList<DeviceInfo> QueryDevices(DeviceQueryType type)
    {
        var kind = type == DeviceQueryType.Ndi ? "NDI" : "WEBCAM";
        var json = ReadNativeJson(ctx => NativeMethods.engine_enumerate_devices(ctx, kind));
        if (string.IsNullOrEmpty(json))
        {
            return [];
        }

        var devices = JsonSerializer.Deserialize<List<DeviceInfo>>(json, Json) ?? [];
        // dshow device names can carry trailing CR/LF; normalize for display and stable ids.
        return devices.Select(d => d with { Name = d.Name.Trim() }).ToList();
    }

    /// <summary>Runs a native call that returns a pointer to an engine-owned UTF-8 buffer and copies the
    /// result out before releasing the gate, since the next such call overwrites that buffer. Only the
    /// copy is under the lock — deserialization is not.</summary>
    private string? ReadNativeJson(Func<IntPtr, IntPtr> call)
    {
        var ctx = _ctx;
        if (ctx == IntPtr.Zero)
        {
            return null;
        }

        lock (_nativeQueryGate)
        {
            return Marshal.PtrToStringUTF8(call(ctx));
        }
    }

    public void AddSource(SourceDefinition source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var channel = _channelById.GetOrAdd(source.Id, _ => Interlocked.Increment(ref _nextChannel));
        var info = new SourceInfo(channel, source.Name, ToProtocol(source.Type), null, SourceStatus.Disconnected, source.Id, channel);
        _sourcesById[source.Id] = info;

        var settings = source.Type switch
        {
            SourceType.Ndi => JsonSerializer.Serialize(source.Ndi, Json),
            SourceType.Webcam => JsonSerializer.Serialize(source.Webcam, Json),
            SourceType.Srt => JsonSerializer.Serialize(source.Srt, Json),
            SourceType.Image => JsonSerializer.Serialize(source.Image, Json),
            SourceType.Html => JsonSerializer.Serialize(source.Html, Json),
            SourceType.Mix => JsonSerializer.Serialize(source.Mix, Json),
            _ => "{}",
        };

        RequireCtx();
        var rc = NativeMethods.engine_add_source(_ctx, source.Id, source.Type.ToString().ToUpperInvariant(), settings);
        if (rc != 0)
        {
            // Native creation failed (rc 3 = obs_source_create returned null - typically an NDI source when
            // the DistroAV plugin is not installed, or an unknown type). Don't leave a phantom tile for a
            // source that has no native backing; roll back the registry entry and surface a clear error.
            _sourcesById.TryRemove(source.Id, out _);
            _channelById.TryRemove(source.Id, out _);

            throw new InvalidOperationException(
                $"Native engine could not create source '{source.Id}' (type {source.Type}, code {rc}). " +
                source.Type switch
                {
                    SourceType.Ndi => "NDI requires the DistroAV (obs-ndi) OBS plugin to be installed.",
                    SourceType.Html => "HTML sources require the obs-browser plugin, which ships with OBS Studio.",
                    _ => "The source type may be unsupported by the installed OBS plugins.",
                });
        }

        // Enable this source's preview tap so its live pixels reach the UI via GetFrame("SRC:<id>").
        NativeMethods.engine_set_tap(_ctx, $"SRC:{source.Id}", enabled: true);
        SourceStatusChanged?.Invoke(this, info);
    }

    public void AddSource(int channel, SourceProtocol protocol, string? sourceUrl)
    {
        var id = $"ch{channel}";
        _channelById[id] = channel;
        BumpNextChannelTo(channel);
        _sourcesById[id] = new SourceInfo(channel, id, protocol, null, SourceStatus.Disconnected, id, channel);

        RequireCtx();
        NativeMethods.engine_add_source(_ctx, id, protocol.ToString().ToUpperInvariant(), sourceUrl is null ? "{}" : JsonSerializer.Serialize(new { url = sourceUrl }, Json));
        NativeMethods.engine_set_tap(_ctx, $"SRC:{id}", enabled: true);
    }

    public void RemoveSource(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        _channelById.TryRemove(id, out _);
        _sourcesById.TryRemove(id, out _);

        // Drop the cached frame too, or a re-added source with the same id shows the removed device's
        // last frame until its tap produces a new one.
        _lastFrameByTarget.TryRemove($"SRC:{id}", out _);

        RequireCtx();
        NativeMethods.engine_remove_source(_ctx, id);
        SourceRemoved?.Invoke(this, id);
    }

    public bool TryResolveChannel(string id, out int channel) => _channelById.TryGetValue(id, out channel);

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
        var id = _channelById.FirstOrDefault(kv => kv.Value == channel).Key;
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

    public void Take(ProgramBus bus, int durationMs)
    {
        RequireCtx();
        // transition_kind: 0 = CUT, 1 = AUTO (native engine_take honors duration only for AUTO).
        NativeMethods.engine_take(_ctx, BusToInt(bus), durationMs > 0 ? 1 : 0, durationMs > 0 ? durationMs : 0);
    }

    public FrameData GetFrame(string target) => _lastFrameByTarget.TryGetValue(target, out var f) ? f : Empty;

    public void ApplyMultiview(MultiviewLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        RequireCtx();
        NativeMethods.engine_apply_multiview(_ctx, JsonSerializer.Serialize(layout, Json));
    }

    public void SetSourceAudioMixers(string id, int mixerMask)
    {
        ArgumentNullException.ThrowIfNull(id);
        RequireCtx();
        NativeMethods.engine_set_source_audio(_ctx, id, mixerMask);
    }

    public IReadOnlyList<OutputStatus> QueryOutputStatus()
    {
        var json = ReadNativeJson(NativeMethods.engine_get_output_status);
        return string.IsNullOrEmpty(json)
            ? []
            : JsonSerializer.Deserialize<OutputStatusReport>(json, Json)?.Outputs ?? [];
    }

    public IReadOnlyList<AudioDeviceInfo> QueryAudioDevices()
    {
        var json = ReadNativeJson(NativeMethods.engine_enumerate_audio_devices);
        return string.IsNullOrEmpty(json)
            ? []
            : JsonSerializer.Deserialize<List<AudioDeviceInfo>>(json, Json) ?? [];
    }

    public void ApplyAudioOutputs(AudioOutputsRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireCtx();
        var rc = NativeMethods.engine_apply_audio_outputs(_ctx, JsonSerializer.Serialize(request, Json));
        if (rc != 0)
        {
            throw new ArgumentException($"Native engine rejected the audio routing (code {rc}).", nameof(request));
        }
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

        _assignments = request.Outputs.ToList();
    }

    public IReadOnlyList<OutputAssignment> CurrentAssignments => _assignments;

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
        lock (_lifecycleGate)
        {
            // Also under the query gate: engine_shutdown frees the buffers a concurrent
            // enumerate/status call would be reading from.
            lock (_nativeQueryGate)
            {
                if (_ctx != IntPtr.Zero)
                {
                    // Blocks until the render callback is removed and no tap callback is in flight, so the
                    // delegates below are unreachable by the time they are dropped.
                    NativeMethods.engine_shutdown(_ctx);
                    _ctx = IntPtr.Zero;
                }
            }

            _frameCallback = null;
            _stateCallback = null;
        }
    }

    /// <summary>Raises the channel counter so a legacy explicit channel number is never handed out again.</summary>
    private void BumpNextChannelTo(int channel)
    {
        var current = Volatile.Read(ref _nextChannel);
        while (channel > current)
        {
            var previous = Interlocked.CompareExchange(ref _nextChannel, channel, current);
            if (previous == current)
            {
                return;
            }

            current = previous;
        }
    }

    private void OnNativeFrame(IntPtr user, string target, IntPtr bgra, int width, int height, int stride)
    {
        if (bgra == IntPtr.Zero || width <= 0 || height <= 0)
        {
            return;
        }

        // The native side hands over a mapped D3D11 staging surface, whose row pitch is driver-chosen and
        // frequently larger than width*4. FrameData carries no stride, and every consumer (WriteableBitmap,
        // virtual camera) assumes tightly packed rows - so compact row by row here rather than shipping the
        // padded buffer, which renders as a diagonally skewed image whenever the pitch is padded.
        var rowBytes = width * 4;
        var buffer = new byte[rowBytes * height];
        if (stride == rowBytes)
        {
            Marshal.Copy(bgra, buffer, 0, buffer.Length);
        }
        else
        {
            for (var y = 0; y < height; y++)
            {
                Marshal.Copy(bgra + (y * stride), buffer, y * rowBytes, rowBytes);
            }
        }

        _lastFrameByTarget[target] = new FrameData(width, height, buffer);

        if (target.StartsWith("SRC:", StringComparison.Ordinal))
        {
            MarkSourceLive(target[4..], width, height);
        }
    }

    /// <summary>Promotes a source to <see cref="SourceStatus.Connected"/> with its real resolution the
    /// first time its tap delivers pixels. Nothing else ever moved a source off
    /// <see cref="SourceStatus.Disconnected"/>, so every tile stayed grey with a "-" resolution however
    /// well the device was actually running.</summary>
    private void MarkSourceLive(string id, int width, int height)
    {
        // Runs on the libobs graphics thread, once per tapped frame. It must not take any lock a slower
        // path could be holding: the native side parks this thread while holding its context lock, so
        // blocking here behind a thread that is waiting for that context lock deadlocks the engine.
        if (!_sourcesById.TryGetValue(id, out var current))
        {
            return;
        }

        var resolution = $"{width}x{height}";
        if (current.Status == SourceStatus.Connected && current.Resolution == resolution)
        {
            return;
        }

        var updated = current with { Status = SourceStatus.Connected, Resolution = resolution };
        if (!_sourcesById.TryUpdate(id, updated, current))
        {
            return;  // removed or updated concurrently; the next frame re-evaluates
        }

        SourceStatusChanged?.Invoke(this, updated);
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

    private static int BusToInt(ProgramBus bus) => bus == ProgramBus.Pgm2 ? 1 : 0;

    private static readonly FrameData Empty = new(0, 0, ReadOnlyMemory<byte>.Empty);

    private static SourceProtocol ToProtocol(SourceType type) => type switch
    {
        SourceType.Ndi => SourceProtocol.Ndi,
        SourceType.Webcam => SourceProtocol.Uvc,
        SourceType.Srt => SourceProtocol.Srt,
        SourceType.Image => SourceProtocol.Image,
        SourceType.Html => SourceProtocol.Html,
        SourceType.Mix => SourceProtocol.Mix,
        _ => SourceProtocol.Uvc,
    };
}
