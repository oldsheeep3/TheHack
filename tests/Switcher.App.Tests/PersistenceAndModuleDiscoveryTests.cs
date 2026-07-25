using System.IO;
using Switcher.Contracts;

namespace Switcher.App.Tests;

/// <summary>
/// Covers the state that has to survive a restart (input sources + multiview layout) and the module
/// list being driven by the Pico controller's <c>module_present</c> bitmap rather than a fixed
/// <see cref="ProtocolConstants.MaxModules"/>.
/// </summary>
public sealed class PersistenceAndModuleDiscoveryTests
{
    private static SourceDefinition Webcam(string id) =>
        new(id, id, SourceType.Webcam, null, new WebcamConfig("device-" + id, "1280x720@30"), null);

    /// <summary>A runtime-config directory owned by the test, so neither harness deletes it between the
    /// simulated shutdown and the restart.</summary>
    private static string CreateSharedConfigDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"switcher-persist-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public async Task SourcesAndMultiviewSurviveARestart()
    {
        var dir = CreateSharedConfigDir();
        MultiviewLayout layout;

        using (var first = new OrchestratorTestHarness(runtimeConfigDir: dir))
        {
            await first.Orchestrator.AddSourceAsync(Webcam("cam-a"));
            await first.Orchestrator.AddSourceAsync(
                new SourceDefinition("logo", "Logo", SourceType.Image, null, null, null, new ImageConfig(@"C:\logo.png"), null));

            layout = new MultiviewLayout(
                [.. Enumerable.Repeat("EMPTY", 16)],
                new MultiviewGrid(2, 2),
                [
                    new MultiviewRegion(0, 0, 1, 1, "PGM1"),
                    new MultiviewRegion(0, 1, 1, 1, "SRC:cam-a"),
                    new MultiviewRegion(1, 0, 1, 2, "SRC:logo"),
                ]);
            await first.Orchestrator.ApplyMultiviewAsync(layout);
        }

        try
        {
            // "Restart": a fresh orchestrator + engine reading the same runtime-config.json.
            using var second = new OrchestratorTestHarness(runtimeConfigDir: dir);
            Assert.Empty(second.Engine.GetSources());

            second.Orchestrator.RestorePersistedSources();

            var restored = second.Engine.GetSources();
            Assert.Equal(2, restored.Count);
            Assert.Contains(restored, s => s.Id == "cam-a" && s.Protocol == SourceProtocol.Uvc);
            Assert.Contains(restored, s => s.Id == "logo" && s.Protocol == SourceProtocol.Image);

            // The multiview layout comes back in its merged-region form, not flattened to 16 cells.
            var current = second.Orchestrator.CurrentMultiviewLayout;
            Assert.Equal(layout.Regions, current.Regions);
            Assert.Equal(layout.Grid, current.Grid);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task MixIsRestoredAfterItsMembersEvenWhenSavedFirst()
    {
        var dir = CreateSharedConfigDir();

        using (var first = new OrchestratorTestHarness(runtimeConfigDir: dir))
        {
            // Add the mix *before* its members so the persisted order is the awkward one.
            await first.Orchestrator.AddSourceAsync(new SourceDefinition(
                "mix", "Mix", SourceType.Mix, null, null, null, null, null,
                new MixConfig([new MixLayer("cam-a", 0, 0, 960, 540)])));
            await first.Orchestrator.AddSourceAsync(Webcam("cam-a"));
        }

        try
        {
            using var second = new OrchestratorTestHarness(runtimeConfigDir: dir);
            second.Orchestrator.RestorePersistedSources();

            // The engine sees the member first; a mix restored before it would have had nothing to
            // reference and would come back empty.
            var order = second.Engine.AddedSourceIds;
            Assert.Equal(["cam-a", "mix"], order);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task RemovedSourceIsNotRestored()
    {
        var dir = CreateSharedConfigDir();
        using (var first = new OrchestratorTestHarness(runtimeConfigDir: dir))
        {
            await first.Orchestrator.AddSourceAsync(Webcam("cam-a"));
            await first.Orchestrator.AddSourceAsync(Webcam("cam-b"));
            await first.Orchestrator.RemoveSourceAsync("cam-a");
        }

        try
        {
            using var second = new OrchestratorTestHarness(runtimeConfigDir: dir);
            second.Orchestrator.RestorePersistedSources();

            var restored = second.Engine.GetSources();
            Assert.Equal("cam-b", Assert.Single(restored).Id);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ModulePresence_CreatesRowsOnlyForAttachedModules()
    {
        using var harness = new OrchestratorTestHarness();
        Assert.Empty(harness.Orchestrator.CurrentModuleMappings);

        // bit 0 and bit 3 set: modules 0 and 3 are attached, nothing else exists.
        harness.Orchestrator.HandleModulePresence(0b0000_1001);

        Assert.Equal([0, 3], harness.Orchestrator.CurrentModuleMappings.Select(m => m.Index));
        Assert.All(harness.Orchestrator.CurrentModuleMappings, m => Assert.Null(m.Src1.SourceId));
    }

    [Fact]
    public async Task ModulePresence_KeepsBindingsOfStillAttachedModulesAndDropsDetachedOnes()
    {
        using var harness = new OrchestratorTestHarness();
        harness.AddTestSource("cam-a");

        harness.Orchestrator.HandleModulePresence(0b0000_0011);  // modules 0 and 1
        await harness.Orchestrator.ApplyModulesAsync(new ModulesRequest(
        [
            new ModuleMapping(0, new ModuleSourceBinding("cam-a", "Assignable"), new ModuleSourceBinding(null, "Assignable")),
            new ModuleMapping(1, new ModuleSourceBinding("cam-a", "Opacity"), new ModuleSourceBinding(null, "Assignable")),
        ]));

        harness.Orchestrator.HandleModulePresence(0b0000_0001);  // module 1 unplugged

        var mapping = Assert.Single(harness.Orchestrator.CurrentModuleMappings);
        Assert.Equal(0, mapping.Index);
        Assert.Equal("cam-a", mapping.Src1.SourceId);
        Assert.Equal([mapping], harness.RuntimeConfigStore.Current.ModuleMappings);
    }

    [Fact]
    public void ModulePresence_UnchangedBitmapDoesNotRaiseModulesChanged()
    {
        using var harness = new OrchestratorTestHarness();
        var raised = 0;
        harness.Orchestrator.ModulesChanged += (_, _) => raised++;

        harness.Orchestrator.HandleModulePresence(0b0000_0101);
        harness.Orchestrator.HandleModulePresence(0b0000_0101);

        Assert.Equal(1, raised);
    }
}
