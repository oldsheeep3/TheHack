using Switcher.Atem;

namespace Switcher.App.Configuration;

/// <summary>
/// Local (App-owned) routing action for a controller button that is not relayed to the ATEM Mini.
/// </summary>
public enum CompositeAction
{
    Take,
    TogglePip,
}

/// <summary>
/// Which M/E rows the operator wants visible in the stage area. Kept as an operator preference (rather
/// than a runtime state) because it is a "how I like the console" choice, not part of a show's state.
/// </summary>
public enum StageViewMode
{
    /// <summary>Both ME1 and ME2 rows visible, splittable. The default.</summary>
    Both,

    /// <summary>Only the ME1 row is shown; the ME2 row and its splitter collapse to zero.</summary>
    Me1Only,

    /// <summary>Only the ME2 row is shown; the ME1 row and its splitter collapse to zero.</summary>
    Me2Only,
}

/// <summary>
/// Maps a (controller_id, button_id) pair to a composite-engine action. Buttons with no entry here
/// fall through to <see cref="IAtemController"/> instead (docs/tasks/agent-A-004-app-integration.md
/// step 2: "合成操作(TAKE/PiP) または ATEM中継"). Channel is only meaningful for <see cref="CompositeAction.TogglePip"/>.
/// </summary>
public sealed record CompositeButtonMapping(string ControllerId, int ButtonId, CompositeAction Action, int? Channel);

/// <summary>
/// Maps a (controller_id, button_id) pair to an ATEM command, handed to <see cref="ButtonCommandMapping"/>.
/// </summary>
public sealed record AtemButtonMapping(string ControllerId, int ButtonId, AtemAction Action, byte MixEffect, ushort Source);

/// <summary>
/// App-level composition settings, loaded from <c>appsettings.json</c> next to the executable (see
/// <see cref="AppConfigLoader"/>). Everything here has a safe default so the app still starts with no
/// config file present.
/// </summary>
public sealed record AppConfig(
    string AtemIp,
    int WebPort,
    int ProjectorDisplayIndex,
    IReadOnlyList<CompositeButtonMapping> CompositeButtonMappings,
    IReadOnlyList<AtemButtonMapping> AtemButtonMappings,
    int OperatorDisplayIndex = 0,
    string? ObsInstallPath = null,
    StageViewMode StageViewMode = Configuration.StageViewMode.Both)
{
    public static AppConfig CreateDefault() => new(
        AtemIp: "192.168.10.240",
        WebPort: Contracts.ProtocolConstants.WebPort,
        ProjectorDisplayIndex: 1,
        OperatorDisplayIndex: 0,
        CompositeButtonMappings:
        [
            new CompositeButtonMapping("main", 0, CompositeAction.Take, null),
            new CompositeButtonMapping("main", 1, CompositeAction.TogglePip, 1),
            new CompositeButtonMapping("main", 2, CompositeAction.TogglePip, 2),
        ],
        AtemButtonMappings:
        [
            new AtemButtonMapping("main", 10, AtemAction.Cut, MixEffect: 0, Source: 1),
            new AtemButtonMapping("main", 11, AtemAction.Auto, MixEffect: 0, Source: 0),
        ]);
}
