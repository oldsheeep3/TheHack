using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using Switcher.App.Orchestration;
using Switcher.Contracts;
using Switcher.Hid.Input;

namespace Switcher.App.ViewModels;

/// <summary>
/// One physical module switch and the ATEM command it relays (docs/specs/00-system-overview.md §4.2
/// <c>PUT /api/v1/atem</c>). A row set to <see cref="AppOrchestrator.NoAtemAction"/> keeps the switch on
/// its normal job — mounting the bound source on a program bus — because the orchestrator only diverts a
/// switch to the ATEM when a mapping exists for it.
/// </summary>
public sealed class AtemAssignmentRowViewModel : INotifyPropertyChanged
{
    /// <summary>ATEM operations offerable per switch, plus the "leave this switch alone" entry.</summary>
    public static IReadOnlyList<string> ActionOptions { get; } =
        [AppOrchestrator.NoAtemAction, "ProgramInput", "PreviewInput", "Cut", "Auto"];

    /// <summary>Shown per mix-effect; the wire value is the 0-based index of the entry.</summary>
    public static IReadOnlyList<string> MixEffectOptions { get; } = ["M/E 1", "M/E 2"];

    private string _action = AppOrchestrator.NoAtemAction;
    private string _mixEffect = MixEffectOptions[0];
    private string _source = "1";

    public AtemAssignmentRowViewModel(int moduleIndex, SwitchId switchId)
    {
        ModuleIndex = moduleIndex;
        Switch = switchId;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public int ModuleIndex { get; }

    public SwitchId Switch { get; }

    /// <summary>What the operator sees on the panel: which bus half of the module this switch is.</summary>
    public string Label => Switch switch
    {
        SwitchId.Pgm1Src1 => "PGM1 · SRC1",
        SwitchId.Pgm1Src2 => "PGM1 · SRC2",
        SwitchId.Pgm2Src1 => "PGM2 · SRC1",
        SwitchId.Pgm2Src2 => "PGM2 · SRC2",
        _ => Switch.ToString(),
    };

    public IReadOnlyList<string> Actions => ActionOptions;

    public IReadOnlyList<string> MixEffects => MixEffectOptions;

    public string Action
    {
        get => _action;
        set
        {
            _action = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(TakesSource));
            OnPropertyChanged(nameof(IsAssigned));
        }
    }

    public string MixEffect
    {
        get => _mixEffect;
        set { _mixEffect = value; OnPropertyChanged(); }
    }

    /// <summary>ATEM input number (1-8 on a Mini; 0 is black). Free text so unusual ids stay reachable.</summary>
    public string Source
    {
        get => _source;
        set { _source = value; OnPropertyChanged(); }
    }

    /// <summary>Cut/Auto act on the whole M/E, so the input number is only meaningful for the two
    /// input-switch actions — the source box hides for the rest rather than inviting a pointless edit.</summary>
    public bool TakesSource => Action is "ProgramInput" or "PreviewInput";

    public bool IsAssigned => !Action.Equals(AppOrchestrator.NoAtemAction, StringComparison.Ordinal);

    /// <summary>The wire mapping for this row, or null when the switch is not relayed to the ATEM.</summary>
    public AtemButtonMapping? ToMapping() => IsAssigned
        ? new AtemButtonMapping(
            ControllerId: "hid",
            ModuleIndex: ModuleIndex,
            Switch: Switch.ToString(),
            Action: Action,
            MixEffect: Math.Max(0, MixEffectOptions.ToList().IndexOf(MixEffect)),
            Source: int.TryParse(Source, NumberStyles.Integer, CultureInfo.InvariantCulture, out var source) ? source : 0)
        : null;

    public void LoadFrom(AtemButtonMapping mapping)
    {
        ArgumentNullException.ThrowIfNull(mapping);

        Action = ActionOptions.FirstOrDefault(a => a.Equals(mapping.Action, StringComparison.OrdinalIgnoreCase))
                 ?? AppOrchestrator.NoAtemAction;
        MixEffect = mapping.MixEffect >= 0 && mapping.MixEffect < MixEffectOptions.Count
            ? MixEffectOptions[mapping.MixEffect]
            : MixEffectOptions[0];
        Source = mapping.Source.ToString(CultureInfo.InvariantCulture);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
