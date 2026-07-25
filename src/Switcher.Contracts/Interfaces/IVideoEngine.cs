namespace Switcher.Contracts;

/// <summary>
/// Startup parameters for the video engine (docs/specs/libobs-engine-migration.md §2.1).
/// <paramref name="ModulePath"/> points at the directory of OBS bundled modules to load
/// (win-dshow / obs-ffmpeg / image-source / text / DistroAV); null lets the native layer use its default.
///
/// The remaining path knobs are the libobs runtime locations the native <c>engine_startup</c> needs; each
/// serializes to the exact <c>options_json</c> key the native layer reads (see native/switcher-engine/README.md).
/// A null field serializes to a JSON null, which the native layer treats as absent and falls back to its
/// <c>SWITCHER_OBS_*</c> environment variables for. <paramref name="DataPath"/> (libobs core data,
/// <c>&lt;obs&gt;/data/libobs/</c>) is <b>required</b> for startup — without it libobs cannot load its built-in
/// effects (<c>default.effect</c>) and <c>obs_reset_video</c> fails, surfacing as
/// "Native switcher-engine failed to start". The App populates these by locating the installed OBS runtime.
/// </summary>
public sealed record EngineOptions(
    int CanvasWidth = EngineDefaults.CanvasWidth,
    int CanvasHeight = EngineDefaults.CanvasHeight,
    int Fps = EngineDefaults.Fps,
    string? ModulePath = null,
    string? DataPath = null,
    string? ModuleBinPath = null,
    string? ModuleDataPath = null,
    string? GraphicsModule = null);

/// <summary>
/// The single video-engine boundary for the switcher (docs/specs/libobs-engine-migration.md §2.1).
/// Replaces the GStreamer/DirectX-era source-manager / compositor / virtual-camera trio with one
/// declarative surface that <c>Switcher.App</c> and
/// <c>Switcher.Web</c> depend on. The real implementation (<c>Switcher.Engine.LibObsVideoEngine</c>)
/// drives libobs through P/Invoke; tests inject <c>Switcher.Engine.FakeVideoEngine</c>.
///
/// The dual M/E model (PGM1/PVW1 = <see cref="ProgramBus.Pgm1"/>, PGM2/PVW2 = <see cref="ProgramBus.Pgm2"/>)
/// shares a single instance of each input source across both buses (libobs <c>obs_view</c>×2).
/// </summary>
public interface IVideoEngine
{
    // --- lifecycle ---------------------------------------------------------

    /// <summary>Boots the engine (libobs startup, module load, dual views). Idempotent no-op if already started.</summary>
    Task StartAsync(EngineOptions options, CancellationToken cancellationToken = default);

    /// <summary>Tears the engine down and releases native resources.</summary>
    Task StopAsync(CancellationToken cancellationToken = default);

    // --- source management (was IInputSourceManager) -----------------------

    IReadOnlyList<SourceInfo> GetSources();

    /// <summary>Enumerates selectable input devices for a source kind via libobs source-property lists
    /// (webcam → win-dshow, NDI → DistroAV). Returns an empty list when the backing module is absent
    /// (e.g. NDI without DistroAV installed) or the engine has not started.</summary>
    IReadOnlyList<DeviceInfo> QueryDevices(DeviceQueryType type);

    /// <summary>Adds/updates an OBS-style source (opened once, shared by both buses).</summary>
    void AddSource(SourceDefinition source);

    /// <summary>Legacy single-channel add retained for the v1 <c>ConfigChangeRequest</c> compat path.</summary>
    void AddSource(int channel, SourceProtocol protocol, string? sourceUrl);

    void RemoveSource(string id);

    /// <summary>Maps a source id to its stable integer channel (tally/backlight math key).</summary>
    bool TryResolveChannel(string id, out int channel);

    event EventHandler<SourceInfo>? SourceStatusChanged;

    /// <summary>Raised with the source id after a source leaves the registry, whichever path removed it
    /// (operator window, Web API, ...). Without it a UI that only prunes its own list on its own button
    /// keeps showing tiles for sources the engine no longer has.</summary>
    event EventHandler<string>? SourceRemoved;

    // --- 2-bus M/E (was ICompositorEngine) ---------------------------------

    /// <summary>Mounts/unmounts a source on the given program bus (hot module-switch operation).</summary>
    void SetSourceEnabled(ProgramBus bus, string sourceId, bool enabled);

    /// <summary>Applies a full PGM/PVW layer set for one bus, optionally taking PVW→PGM.</summary>
    void ApplyProgram(ProgramRequest request);

    /// <summary>Legacy single-channel PiP update retained for the v1 compat path.</summary>
    void ApplyPipSettings(int channel, PipSettings settings);

    /// <summary>Legacy global TAKE retained for the v1 composite-button path.</summary>
    void Take();

    /// <summary>Takes PVW→PGM on a single bus, leaving the other bus untouched.
    /// <paramref name="durationMs"/> of 0 is a hard CUT; a positive value runs the bus's fade
    /// transition over that many milliseconds (AUTO).</summary>
    void Take(ProgramBus bus, int durationMs);

    // --- preview taps (App WriteableBitmap pump) ---------------------------

    /// <summary>Latest composited frame for a target token: <c>PGM1</c>/<c>PGM2</c>/<c>PVW1</c>/<c>PVW2</c>/
    /// <c>MULTIVIEW</c>/<c>SRC:&lt;id&gt;</c>. Returns an empty frame when nothing has been rendered yet.</summary>
    FrameData GetFrame(string target);

    // --- multiview ---------------------------------------------------------

    void ApplyMultiview(MultiviewLayout layout);

    // --- audio -------------------------------------------------------------

    /// <summary>Sets which program buses hear a source, as a bitmask (bit 0 = PGM1, bit 1 = PGM2).
    /// The caller owns the AFV/ON/OFF policy and recomputes the mask as bus membership changes; the
    /// engine only applies it.</summary>
    void SetSourceAudioMixers(string id, int mixerMask);

    /// <summary>Audio render endpoints this machine can play to.</summary>
    IReadOnlyList<AudioDeviceInfo> QueryAudioDevices();

    /// <summary>Routes buses to audio devices, replacing the whole table. A bus may be routed to more
    /// than one device.</summary>
    void ApplyAudioOutputs(AudioOutputsRequest request);

    // --- outputs (was IVirtualCameraOutput + OutputRouter) -----------------

    /// <summary>Applies the VCAM1/VCAM2/HDMI/NDI1/NDI2 sink assignments atomically.</summary>
    void ApplyOutputs(OutputsRequest request);

    IReadOnlyList<OutputAssignment> CurrentAssignments { get; }

    /// <summary>Which of the assigned sinks are actually egressing. An accepted assignment can still
    /// fail to start (no NDI runtime, virtual camera in use elsewhere), which is invisible in
    /// <see cref="CurrentAssignments"/>.</summary>
    IReadOnlyList<OutputStatus> QueryOutputStatus();

    /// <summary>Starts a fullscreen <c>obs_display</c> for <paramref name="target"/> (e.g. "PGM1"/"MULTIVIEW")
    /// on the given window handle and display. No-op on backends without native display support.</summary>
    void StartDisplayOutput(string target, IntPtr windowHandle, int displayId);

    void StopDisplayOutput(string target);
}
