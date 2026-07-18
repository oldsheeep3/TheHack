using System.IO;
using Microsoft.Extensions.Logging.Abstractions;
using Switcher.App.Configuration;
using Switcher.App.Orchestration;
using Switcher.Atem;
using Switcher.Contracts;
using Switcher.Hid;
using Switcher.Media;
using Switcher.VirtualCam;

namespace Switcher.App.Tests;

/// <summary>
/// Builds a real <see cref="AppOrchestrator"/> wired to real (but never-started/never-network-connected)
/// module instances - the same shape <see cref="Composition.ServiceCollectionExtensions"/> assembles in
/// production - so tests exercise the actual integration wiring rather than a hand-rolled substitute.
/// The only fake is <see cref="FakeTallyBroadcaster"/> (avoids a real UDP broadcast socket per test).
/// <see cref="InputSourceManager"/>'s background GStreamer pipeline threads never block source
/// registration (channel/id allocation happens synchronously before the thread starts) so they don't
/// affect assertions; <see cref="Dispose"/> cancels/joins them.
/// </summary>
internal sealed class OrchestratorTestHarness : IDisposable
{
    private readonly string _runtimeConfigDir;

    public OrchestratorTestHarness(AppConfig? config = null)
    {
        Config = config ?? AppConfig.CreateDefault();

        SourceManager = new InputSourceManager(NullLoggerFactory.Instance);
        Compositor = new CompositorEngine(SourceManager);
        AtemController = new AtemController(ButtonCommandMapping.Empty, NullLogger<AtemController>.Instance);
        TallyBroadcaster = new FakeTallyBroadcaster();
        VirtualCameraOutput = new DualVirtualCameraOutput();
        OutputRouter = new OutputRouter(VirtualCameraOutput);
        HidBacklightService = new HidBacklightService();

        _runtimeConfigDir = Path.Combine(Path.GetTempPath(), $"switcher-app-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_runtimeConfigDir);
        RuntimeConfigStore = new RuntimeConfigStore(_runtimeConfigDir, NullLogger.Instance);

        Orchestrator = new AppOrchestrator(
            SourceManager,
            Compositor,
            AtemController,
            TallyBroadcaster,
            OutputRouter,
            HidBacklightService,
            RuntimeConfigStore,
            Config,
            NullLogger<AppOrchestrator>.Instance);
    }

    public AppConfig Config { get; }

    public InputSourceManager SourceManager { get; }

    public CompositorEngine Compositor { get; }

    public AtemController AtemController { get; }

    public FakeTallyBroadcaster TallyBroadcaster { get; }

    public DualVirtualCameraOutput VirtualCameraOutput { get; }

    public OutputRouter OutputRouter { get; }

    public HidBacklightService HidBacklightService { get; }

    public RuntimeConfigStore RuntimeConfigStore { get; }

    public AppOrchestrator Orchestrator { get; }

    /// <summary>Registers a v2 (id-based) source and returns its allocated channel. Registration
    /// (id/channel bookkeeping) is synchronous; the underlying capture pipeline connects (or fails and
    /// retries) on its own background thread and never blocks this call.</summary>
    public int AddTestSource(string id)
    {
        SourceManager.AddSource(new SourceDefinition(id, id, SourceType.Webcam, null, new WebcamConfig("fake-device", null), null));
        SourceManager.TryResolveChannel(id, out var channel);
        return channel;
    }

    public void Dispose()
    {
        SourceManager.Dispose();
        Compositor.Dispose();
        AtemController.Dispose();
        VirtualCameraOutput.Dispose();
        HidBacklightService.Dispose();

        try
        {
            Directory.Delete(_runtimeConfigDir, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort cleanup only.
        }
    }
}
