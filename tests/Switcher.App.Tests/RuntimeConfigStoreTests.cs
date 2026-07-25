using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Switcher.App.Configuration;
using Switcher.Contracts;

namespace Switcher.App.Tests;

/// <summary>
/// Persistence durability. This file now holds everything the operator configured — sources, multiview
/// layout, output and audio routing, tally colours — so losing it or failing to write it are both
/// production incidents, and a save runs on whichever thread caused the change (including the HID read
/// loop, where an escaping exception ends the process).
/// </summary>
public sealed class RuntimeConfigStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"switcher-config-tests-{Guid.NewGuid():N}");

    public RuntimeConfigStoreTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
            // Best effort on a temp directory.
        }
    }

    private RuntimeConfigStore NewStore() => new(_dir, NullLogger.Instance);

    [Fact]
    public void Save_WritesAtomicallyAndKeepsThePreviousVersionAsBackup()
    {
        var store = NewStore();
        store.Save(store.Current with { MultiviewCells = Enumerable.Repeat("PGM1", 16).ToList() });
        store.Save(store.Current with { MultiviewCells = Enumerable.Repeat("PGM2", 16).ToList() });

        Assert.True(File.Exists(Path.Combine(_dir, RuntimeConfigStore.FileName)));
        Assert.True(File.Exists(Path.Combine(_dir, RuntimeConfigStore.FileName + ".bak")));

        // No temporary file is left behind, so a later reader never sees a half-written document.
        Assert.False(File.Exists(Path.Combine(_dir, RuntimeConfigStore.FileName + ".tmp")));
    }

    [Fact]
    public void Load_FallsBackToTheBackupWhenTheMainFileIsCorrupt()
    {
        var store = NewStore();
        store.Save(store.Current with { MultiviewCells = Enumerable.Repeat("PGM1", 16).ToList() });
        store.Save(store.Current with { MultiviewCells = Enumerable.Repeat("PGM2", 16).ToList() });

        // What a crash or power loss during the write used to leave behind.
        File.WriteAllText(Path.Combine(_dir, RuntimeConfigStore.FileName), "{\"multiview_cells\": [\"PG");

        var reloaded = NewStore();

        Assert.Equal("PGM1", Assert.Single(reloaded.Current.MultiviewCells.Distinct()));
    }

    [Fact]
    public void Load_UsesDefaultsWhenNothingIsReadable()
    {
        File.WriteAllText(Path.Combine(_dir, RuntimeConfigStore.FileName), "not json at all");

        var store = NewStore();

        Assert.Equal(RuntimeConfig.MultiviewCellCount, store.Current.MultiviewCells.Count);
        Assert.Equal(OutputDefaults.Default, store.Current.OutputAssignments);
    }

    [Fact]
    public void Save_DoesNotThrowWhenTheDirectoryCannotBeWritten()
    {
        var store = NewStore();

        // Stand in for an install directory a standard user cannot write to: the target path is a
        // directory, so every write attempt fails with UnauthorizedAccessException.
        var blocked = Path.Combine(_dir, "blocked");
        Directory.CreateDirectory(Path.Combine(blocked, RuntimeConfigStore.FileName));
        var blockedStore = new RuntimeConfigStore(blocked, NullLogger.Instance);

        var exception = Record.Exception(() => blockedStore.Save(blockedStore.Current with { MultiviewCells = ["PGM1"] }));

        Assert.Null(exception);

        // The change is still applied in memory — only its persistence was lost.
        Assert.Equal(["PGM1"], blockedStore.Current.MultiviewCells);
    }

    [Fact]
    public void Save_RoundTripsThroughDiskUnchanged()
    {
        var store = NewStore();
        var config = store.Current with
        {
            Sources = [new SourceDefinition("cam-a", "Cam A", SourceType.Webcam, null, new WebcamConfig("dev", null), null)],
            AudioOutputs = [new AudioOutputAssignment(ProgramBus.Pgm2, "device-b", "Stream")],
            TallyColors = new TallyColors(
                Pgm1: new BacklightColor(200, 10, 10),
                Pgm2: new BacklightColor(200, 90, 0),
                Pvw1: new BacklightColor(10, 200, 10),
                Pvw2: new BacklightColor(0, 180, 150),
                Idle: new BacklightColor(20, 20, 20)),
        };

        store.Save(config);
        var reloaded = NewStore().Current;

        Assert.Equal(config.SourceList, reloaded.SourceList);
        Assert.Equal(config.AudioOutputList, reloaded.AudioOutputList);
        Assert.Equal(config.Tally, reloaded.Tally);
    }

    [Fact]
    public void MigrateLegacyFile_MovesAConfigWrittenNextToTheExecutable()
    {
        var legacy = Path.Combine(_dir, "legacy");
        var data = Path.Combine(_dir, "data");
        Directory.CreateDirectory(legacy);
        Directory.CreateDirectory(data);

        var previous = RuntimeConfig.CreateDefault() with { MultiviewCells = Enumerable.Repeat("PGM2", 16).ToList() };
        File.WriteAllText(
            Path.Combine(legacy, RuntimeConfigStore.FileName),
            JsonSerializer.Serialize(previous, ProtocolJsonOptions.Default));

        RuntimeConfigMigration.MigrateLegacyFile(legacy, data, NullLogger.Instance);

        Assert.Equal("PGM2", Assert.Single(new RuntimeConfigStore(data, NullLogger.Instance).Current.MultiviewCells.Distinct()));
    }

    [Fact]
    public void MigrateLegacyFile_NeverOverwritesAnExistingConfiguration()
    {
        var legacy = Path.Combine(_dir, "legacy");
        var data = Path.Combine(_dir, "data");
        Directory.CreateDirectory(legacy);
        Directory.CreateDirectory(data);

        File.WriteAllText(
            Path.Combine(legacy, RuntimeConfigStore.FileName),
            JsonSerializer.Serialize(
                RuntimeConfig.CreateDefault() with { MultiviewCells = Enumerable.Repeat("PGM2", 16).ToList() },
                ProtocolJsonOptions.Default));

        var current = new RuntimeConfigStore(data, NullLogger.Instance);
        current.Save(current.Current with { MultiviewCells = Enumerable.Repeat("PVW1", 16).ToList() });

        RuntimeConfigMigration.MigrateLegacyFile(legacy, data, NullLogger.Instance);

        Assert.Equal("PVW1", Assert.Single(new RuntimeConfigStore(data, NullLogger.Instance).Current.MultiviewCells.Distinct()));
    }
}
