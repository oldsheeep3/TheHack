using Switcher.Contracts;

namespace Switcher.Atem;

/// <summary>
/// Configurable controller_id+button_id -> ATEM command mapping table
/// (docs/tasks/agent-A-003-atem-control.md step 2). Immutable; callers wanting to change bindings at
/// runtime (e.g. the App integration task loading operator configuration) build a new instance and
/// hand it to <see cref="AtemController.SetMapping"/>.
/// </summary>
public sealed class ButtonCommandMapping(IReadOnlyDictionary<(string ControllerId, int ButtonId), AtemCommandMapping> mappings)
{
    public static ButtonCommandMapping Empty { get; } = new(new Dictionary<(string, int), AtemCommandMapping>());

    public bool TryGetMapping(ButtonEvent buttonEvent, out AtemCommandMapping mapping) =>
        mappings.TryGetValue((buttonEvent.ControllerId, buttonEvent.ButtonId), out mapping!);
}
