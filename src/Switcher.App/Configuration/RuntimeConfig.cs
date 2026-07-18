using Switcher.Contracts;
using Switcher.VirtualCam;

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
    PicoNetworkConfig? PicoNetwork)
{
    public const int MultiviewCellCount = 16;

    public static RuntimeConfig CreateDefault() => new(
        ModuleMappings: [],
        MultiviewCells: Enumerable.Repeat("EMPTY", MultiviewCellCount).ToList(),
        OutputAssignments: OutputRouter.DefaultAssignments,
        AtemConfig: new AtemConfig(Enabled: false, Ip: string.Empty, Mappings: []),
        PicoNetwork: null);
}
