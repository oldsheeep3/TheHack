namespace Switcher.Contracts;

/// <summary>
/// Startup parameters for the video engine (docs/specs/libobs-engine-migration.md §2.1).
/// <paramref name="ModulePath"/> points at the directory of OBS bundled modules to load
/// (win-dshow / obs-ffmpeg / image-source / text / DistroAV); null lets the native layer use its default.
/// </summary>
public sealed record EngineOptions(
    int CanvasWidth = EngineDefaults.CanvasWidth,
    int CanvasHeight = EngineDefaults.CanvasHeight,
    int Fps = EngineDefaults.Fps,
    string? ModulePath = null);

/// <summary>
/// The single video-engine boundary for the switcher (docs/specs/libobs-engine-migration.md §2.1).
/// Replaces the GStreamer/DirectX-era <see cref="IInputSourceManager"/> / <see cref="ICompositorEngine"/> /
/// <see cref="IVirtualCameraOutput"/> trio with one declarative surface that <c>Switcher.App</c> and
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

    /// <summary>Adds/updates an OBS-style source (opened once, shared by both buses).</summary>
    void AddSource(SourceDefinition source);

    /// <summary>Legacy single-channel add retained for the v1 <c>ConfigChangeRequest</c> compat path.</summary>
    void AddSource(int channel, SourceProtocol protocol, string? sourceUrl);

    void RemoveSource(string id);

    /// <summary>Maps a source id to its stable integer channel (tally/backlight math key).</summary>
    bool TryResolveChannel(string id, out int channel);

    event EventHandler<SourceInfo>? SourceStatusChanged;

    // --- 2-bus M/E (was ICompositorEngine) ---------------------------------

    /// <summary>Mounts/unmounts a source on the given program bus (hot module-switch operation).</summary>
    void SetSourceEnabled(ProgramBus bus, string sourceId, bool enabled);

    /// <summary>Applies a full PGM/PVW layer set for one bus, optionally taking PVW→PGM.</summary>
    void ApplyProgram(ProgramRequest request);

    /// <summary>Legacy single-channel PiP update retained for the v1 compat path.</summary>
    void ApplyPipSettings(int channel, PipSettings settings);

    /// <summary>Legacy global TAKE retained for the v1 composite-button path.</summary>
    void Take();

    // --- preview taps (App WriteableBitmap pump) ---------------------------

    FrameData GetProgramFrame();

    FrameData GetPreviewFrame();

    // --- multiview ---------------------------------------------------------

    void ApplyMultiview(MultiviewLayout layout);

    // --- outputs (was IVirtualCameraOutput + OutputRouter) -----------------

    /// <summary>Applies the VCAM1/VCAM2/HDMI/NDI1/NDI2 sink assignments atomically.</summary>
    void ApplyOutputs(OutputsRequest request);

    IReadOnlyList<OutputAssignment> CurrentAssignments { get; }

    /// <summary>Starts a fullscreen <c>obs_display</c> for <paramref name="target"/> (e.g. "PGM1"/"MULTIVIEW")
    /// on the given window handle and display. No-op on backends without native display support.</summary>
    void StartDisplayOutput(string target, IntPtr windowHandle, int displayId);

    void StopDisplayOutput(string target);
}
