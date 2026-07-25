using Switcher.Contracts;

namespace Switcher.App.Tests;

/// <summary>
/// The audio-follows-video policy and the per-bus device routing.
///
/// libobs has no AFV concept — a source is in an audio track or it isn't — so the orchestrator owns the
/// rule and recomputes masks whenever what is on air changes. These tests pin that rule, because a
/// mistake in either direction is bad live: audio that never opens, or a source heard while it is only
/// being cued.
/// </summary>
public sealed class AudioRoutingTests
{
    private const int Pgm1 = 1 << 0;
    private const int Pgm2 = 1 << 1;

    private static PipSettings FullFrame => new(true, 0, 0, 1920, 1080, 1.0, 0, null);

    private static SourceDefinition Cam(string id, SourceAudioMode mode) =>
        new(id, id, SourceType.Webcam, null, new WebcamConfig("dev-" + id, null), null, AudioMode: mode);

    [Fact]
    public void NewSourceDefaultsToAudioFollowsVideo() =>
        Assert.Equal(SourceAudioMode.Afv,
            new SourceDefinition("s", "s", SourceType.Webcam, null, new WebcamConfig("d", null), null).AudioMode);

    [Fact]
    public async Task AfvSource_IsSilentUntilItGoesOnAir()
    {
        using var harness = new OrchestratorTestHarness();
        await harness.Orchestrator.AddSourceAsync(Cam("cam-a", SourceAudioMode.Afv));

        Assert.Equal(0, harness.Engine.AudioMixersFor("cam-a"));

        await harness.Orchestrator.ApplyProgramAsync(
            new ProgramRequest(ProgramBus.Pgm1, [new ProgramLayer("cam-a", FullFrame)], Take: true));

        Assert.Equal(Pgm1, harness.Engine.AudioMixersFor("cam-a"));
    }

    [Fact]
    public async Task AfvSource_StagedOnPreviewStaysSilent()
    {
        using var harness = new OrchestratorTestHarness();
        await harness.Orchestrator.AddSourceAsync(Cam("cam-a", SourceAudioMode.Afv));

        await harness.Orchestrator.ApplyProgramAsync(
            new ProgramRequest(ProgramBus.Pgm1, [new ProgramLayer("cam-a", FullFrame)], Take: false));

        // Cueing a shot must not put its audio on air.
        Assert.Equal(0, harness.Engine.AudioMixersFor("cam-a"));
    }

    [Fact]
    public async Task AfvSource_GoesSilentAgainWhenTakenOffAir()
    {
        using var harness = new OrchestratorTestHarness();
        await harness.Orchestrator.AddSourceAsync(Cam("cam-a", SourceAudioMode.Afv));
        await harness.Orchestrator.ApplyProgramAsync(
            new ProgramRequest(ProgramBus.Pgm1, [new ProgramLayer("cam-a", FullFrame)], Take: true));

        await harness.Orchestrator.ApplyProgramAsync(new ProgramRequest(ProgramBus.Pgm1, [], Take: true));

        Assert.Equal(0, harness.Engine.AudioMixersFor("cam-a"));
    }

    [Fact]
    public async Task AfvSource_OnBothBuses_IsHeardOnBoth()
    {
        using var harness = new OrchestratorTestHarness();
        await harness.Orchestrator.AddSourceAsync(Cam("cam-a", SourceAudioMode.Afv));

        await harness.Orchestrator.ApplyProgramAsync(
            new ProgramRequest(ProgramBus.Pgm1, [new ProgramLayer("cam-a", FullFrame)], Take: true));
        await harness.Orchestrator.ApplyProgramAsync(
            new ProgramRequest(ProgramBus.Pgm2, [new ProgramLayer("cam-a", FullFrame)], Take: true));

        Assert.Equal(Pgm1 | Pgm2, harness.Engine.AudioMixersFor("cam-a"));
    }

    [Fact]
    public async Task OnSource_IsHeardOnBothBusesWithoutBeingOnAir()
    {
        using var harness = new OrchestratorTestHarness();
        await harness.Orchestrator.AddSourceAsync(Cam("music", SourceAudioMode.On));

        // A backing track or a presenter mic is audible regardless of what picture is live.
        Assert.Equal(Pgm1 | Pgm2, harness.Engine.AudioMixersFor("music"));
    }

    [Fact]
    public async Task OffSource_IsNeverHeard()
    {
        using var harness = new OrchestratorTestHarness();
        await harness.Orchestrator.AddSourceAsync(Cam("cam-a", SourceAudioMode.Off));

        await harness.Orchestrator.ApplyProgramAsync(
            new ProgramRequest(ProgramBus.Pgm1, [new ProgramLayer("cam-a", FullFrame)], Take: true));

        Assert.Equal(0, harness.Engine.AudioMixersFor("cam-a"));
    }

    [Fact]
    public async Task SourceInsideALiveMix_IsHeard()
    {
        using var harness = new OrchestratorTestHarness();
        await harness.Orchestrator.AddSourceAsync(Cam("cam-a", SourceAudioMode.Afv));
        await harness.Orchestrator.AddSourceAsync(new SourceDefinition(
            "mix", "Mix", SourceType.Mix, null, null, null, null, null,
            new MixConfig([new MixLayer("cam-a", 0, 0, 1920, 1080)])));

        await harness.Orchestrator.ApplyProgramAsync(
            new ProgramRequest(ProgramBus.Pgm1, [new ProgramLayer("mix", FullFrame)], Take: true));

        // The camera's picture is being broadcast through the mix, so its audio must follow it.
        Assert.Equal(Pgm1, harness.Engine.AudioMixersFor("cam-a"));
    }

    [Fact]
    public async Task ChangingTheModeTakesEffectOnTheNextRecompute()
    {
        using var harness = new OrchestratorTestHarness();
        await harness.Orchestrator.AddSourceAsync(Cam("cam-a", SourceAudioMode.Off));
        await harness.Orchestrator.ApplyProgramAsync(
            new ProgramRequest(ProgramBus.Pgm1, [new ProgramLayer("cam-a", FullFrame)], Take: true));
        Assert.Equal(0, harness.Engine.AudioMixersFor("cam-a"));

        await harness.Orchestrator.UpdateSourceAsync("cam-a", Cam("cam-a", SourceAudioMode.On));
        harness.Orchestrator.RestorePersistedAudio();

        Assert.Equal(Pgm1 | Pgm2, harness.Engine.AudioMixersFor("cam-a"));
    }

    // --- device routing ---------------------------------------------------------

    [Fact]
    public async Task AudioOutputs_ArePersistedAndReapplied()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"switcher-audio-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);

        var outputs = new List<AudioOutputAssignment>
        {
            new(ProgramBus.Pgm1, "device-a", "Front of house"),
            new(ProgramBus.Pgm2, "device-b", "Stream"),
        };

        try
        {
            using (var first = new OrchestratorTestHarness(runtimeConfigDir: dir))
            {
                await first.Orchestrator.ApplyAudioOutputsAsync(new AudioOutputsRequest(outputs));
                Assert.Equal(outputs, first.Engine.CurrentAudioOutputs);
            }

            using var second = new OrchestratorTestHarness(runtimeConfigDir: dir);
            second.Orchestrator.RestorePersistedAudio();

            Assert.Equal(outputs, second.Engine.CurrentAudioOutputs);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task OneBusMayFeedSeveralDevices()
    {
        using var harness = new OrchestratorTestHarness();
        var outputs = new List<AudioOutputAssignment>
        {
            new(ProgramBus.Pgm1, "front-of-house"),
            new(ProgramBus.Pgm1, "recorder"),
            new(ProgramBus.Pgm2, "stream"),
        };

        await harness.Orchestrator.ApplyAudioOutputsAsync(new AudioOutputsRequest(outputs));

        Assert.Equal(2, harness.Engine.CurrentAudioOutputs.Count(o => o.Bus == ProgramBus.Pgm1));
        Assert.Single(harness.Engine.CurrentAudioOutputs, o => o.Bus == ProgramBus.Pgm2);
    }

    [Fact]
    public async Task ApplyingAnEmptyTable_SilencesEverything()
    {
        using var harness = new OrchestratorTestHarness();
        await harness.Orchestrator.ApplyAudioOutputsAsync(
            new AudioOutputsRequest([new AudioOutputAssignment(ProgramBus.Pgm1, "device-a")]));

        await harness.Orchestrator.ApplyAudioOutputsAsync(new AudioOutputsRequest([]));

        Assert.Empty(harness.Engine.CurrentAudioOutputs);
    }
}
