using System.IO;
using Microsoft.Extensions.Logging.Abstractions;
using Switcher.App.Configuration;
using Switcher.App.Orchestration;
using Switcher.Atem;
using Switcher.Contracts;
using Switcher.Engine;
using Switcher.Hid;

namespace Switcher.App.Tests;

/// <summary>
/// Builds a real <see cref="AppOrchestrator"/> wired to a <see cref="FakeVideoEngine"/> (the same
/// <see cref="IVideoEngine"/> seam <see cref="Composition.ServiceCollectionExtensions"/> assembles in
/// production, minus the native libobs backend) plus the other real module instances, so tests exercise
/// the actual orchestration wiring rather than a hand-rolled substitute. The only other fake is
/// <see cref="FakeTallyBroadcaster"/> (avoids a real UDP broadcast socket per test).
/// </summary>
internal sealed class OrchestratorTestHarness : IDisposable
{
    private readonly string _runtimeConfigDir;

    public OrchestratorTestHarness(AppConfig? config = null)
    {
        Config = config ?? AppConfig.CreateDefault();

        Engine = new FakeVideoEngine();
        AtemController = new AtemController(ButtonCommandMapping.Empty, NullLogger<AtemController>.Instance);
        TallyBroadcaster = new FakeTallyBroadcaster();
        HidBacklightService = new HidBacklightService();

        _runtimeConfigDir = Path.Combine(Path.GetTempPath(), $"switcher-app-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_runtimeConfigDir);
        RuntimeConfigStore = new RuntimeConfigStore(_runtimeConfigDir, NullLogger.Instance);

        Orchestrator = new AppOrchestrator(
            Engine,
            AtemController,
            TallyBroadcaster,
            HidBacklightService,
            RuntimeConfigStore,
            Config,
            NullLogger<AppOrchestrator>.Instance);
    }

    public AppConfig Config { get; }

    public FakeVideoEngine Engine { get; }

    public AtemController AtemController { get; }

    public FakeTallyBroadcaster TallyBroadcaster { get; }

    public HidBacklightService HidBacklightService { get; }

    public RuntimeConfigStore RuntimeConfigStore { get; }

    public AppOrchestrator Orchestrator { get; }

    /// <summary>Registers a v2 (id-based) source and returns its allocated channel.</summary>
    public int AddTestSource(string id)
    {
        Engine.AddSource(new SourceDefinition(id, id, SourceType.Webcam, null, new WebcamConfig("fake-device", null), null));
        Engine.TryResolveChannel(id, out var channel);
        return channel;
    }

    public void Dispose()
    {
        AtemController.Dispose();
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
