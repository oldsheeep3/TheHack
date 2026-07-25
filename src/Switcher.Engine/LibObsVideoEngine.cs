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

    public event EventHandler<string>? SourceRemoved;

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

    public IReadOnlyList<SourceInfo> GetSources()
    {
        lock (_gate)
        {
            return _sourcesById.Values.OrderBy(s => s.Order ?? s.Channel).ToList();
        }
    }

    public IReadOnlyList<DeviceInfo> QueryDevices(DeviceQueryType type)
    {
        lock (_gate)
        {
            if (_ctx == IntPtr.Zero)
            {
                return [];
            }

            var kind = type == DeviceQueryType.Ndi ? "NDI" : "WEBCAM";
            var ptr = NativeMethods.engine_enumerate_devices(_ctx, kind);
            var json = Marshal.PtrToStringUTF8(ptr);  // copy now; the native buffer is reused next call
            if (string.IsNullOrEmpty(json))
            {
                return [];
            }

            var devices = JsonSerializer.Deserialize<List<DeviceInfo>>(json, Json) ?? [];
            // dshow device names can carry trailing CR/LF; normalize for display and stable ids.
            return devices.Select(d => d with { Name = d.Name.Trim() }).ToList();
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
            lock (_gate)
            {
                _sourcesById.Remove(source.Id);
                _channelById.Remove(source.Id);
            }

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
        NativeMethods.engine_set_tap(_ctx, $"SRC:{id}", enabled: true);
    }

    public void RemoveSource(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        lock (_gate)
        {
            _channelById.Remove(id);
            _sourcesById.Remove(id);
        }

        // Drop the cached frame too, or a re-added source with the same id shows the removed device's
        // last frame until its tap produces a new one.
        _lastFrameByTarget.TryRemove($"SRC:{id}", out _);

        RequireCtx();
        NativeMethods.engine_remove_source(_ctx, id);
        SourceRemoved?.Invoke(this, id);
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
        lock (_gate)
        {
            if (_ctx == IntPtr.Zero)
            {
                return [];
            }

            var ptr = NativeMethods.engine_get_output_status(_ctx);
            var json = Marshal.PtrToStringUTF8(ptr);  // copy now; the native buffer is reused next call
            return string.IsNullOrEmpty(json)
                ? []
                : JsonSerializer.Deserialize<OutputStatusReport>(json, Json)?.Outputs ?? [];
        }
    }

    public IReadOnlyList<AudioDeviceInfo> QueryAudioDevices()
    {
        lock (_gate)
        {
            if (_ctx == IntPtr.Zero)
            {
                return [];
            }

            var ptr = NativeMethods.engine_enumerate_audio_devices(_ctx);
            var json = Marshal.PtrToStringUTF8(ptr);  // copy now; the native buffer is reused next call
            return string.IsNullOrEmpty(json)
                ? []
                : JsonSerializer.Deserialize<List<AudioDeviceInfo>>(json, Json) ?? [];
        }
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
        var resolution = $"{width}x{height}";
        SourceInfo updated;
        lock (_gate)
        {
            if (!_sourcesById.TryGetValue(id, out var current) ||
                (current.Status == SourceStatus.Connected && current.Resolution == resolution))
            {
                return;
            }

            updated = current with { Status = SourceStatus.Connected, Resolution = resolution };
            _sourcesById[id] = updated;
        }

        // Raised outside the lock: this runs on the libobs graphics thread and handlers marshal to their
        // own dispatcher, which must never be able to block frame delivery behind _gate.
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
        SourceType.Image => SourceProtocol.Image,
        SourceType.Html => SourceProtocol.Html,
        SourceType.Mix => SourceProtocol.Mix,
        _ => SourceProtocol.Uvc,
    };
}
