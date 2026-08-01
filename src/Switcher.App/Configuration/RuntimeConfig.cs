using Switcher.Contracts;

namespace Switcher.App.Configuration;

/// <summary>
/// Live operator state driven by the Web/HID control surfaces (module mapping, multiview layout,
/// output routing, ATEM config, Pico network config) - as opposed to <see cref="AppConfig"/>, which
/// only holds the app's own bootstrap settings. Persisted to disk so "PCが設定の正" (docs/specs/pc-switcher-app.md
/// §2.8): restarting the app restores the last-applied configuration instead of losing it.
/// </summary>
public sealed record RuntimeConfig(
    IReadOnlyList<ModuleMapping> ModuleMappings,
    IReadOnlyList<string> MultiviewCells,
    IReadOnlyList<OutputAssignment> OutputAssignments,
    AtemConfig AtemConfig,
    PicoNetworkConfig? PicoNetwork,
    MultiviewGrid? MultiviewGrid = null,
    IReadOnlyList<MultiviewRegion>? MultiviewRegions = null,
    IReadOnlyList<SourceDefinition>? Sources = null,
    TallyColors? TallyColors = null,
    IReadOnlyList<AudioOutputAssignment>? AudioOutputs = null)
{
    public const int MultiviewCellCount = 16;

    /// <summary>The persisted input sources, replayed onto the engine at startup so a restart restores
    /// the same inputs (and therefore the same <c>SRC:&lt;id&gt;</c> multiview cells and module
    /// bindings). Never null once normalized; a config written before this field existed reads as an
    /// empty list.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlyList<SourceDefinition> SourceList => Sources ?? [];

    /// <summary>Tally colours for the UI and the module LEDs; falls back to the broadcast default for a
    /// config written before this field existed.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public TallyColors Tally => TallyColors ?? Switcher.Contracts.TallyColors.CreateDefault();

    /// <summary>Bus-to-device audio routing; empty means no audio is being played out.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlyList<AudioOutputAssignment> AudioOutputList => AudioOutputs ?? [];

    public static RuntimeConfig CreateDefault() => new(
        ModuleMappings: [],
        MultiviewCells: Enumerable.Repeat("EMPTY", MultiviewCellCount).ToList(),
        OutputAssignments: OutputDefaults.Default,
        AtemConfig: new AtemConfig(Enabled: false, Ip: string.Empty, Mappings: []),
        PicoNetwork: null,
        MultiviewGrid: null,
        MultiviewRegions: null,
        Sources: [],
        TallyColors: Switcher.Contracts.TallyColors.CreateDefault(),
        AudioOutputs: []);
}
