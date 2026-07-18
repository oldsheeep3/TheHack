using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Switcher.Contracts;

namespace Switcher.App.ViewModels;

/// <summary>
/// One row of the module-mapping panel: a physical module's Src1/Src2 switch-pair binding
/// (docs/specs/00-system-overview.md §4.2 <c>PUT /api/v1/modules</c>). <c>"(none)"</c> in
/// <see cref="Src1SourceId"/>/<see cref="Src2SourceId"/> means the slot is unbound (serialized as a
/// <c>null</c> <see cref="ModuleSourceBinding.SourceId"/>).
/// </summary>
public sealed class ModuleMappingRowViewModel : INotifyPropertyChanged
{
    public const string NoneSourceId = "(none)";

    private string _src1SourceId = NoneSourceId;
    private string _src1VrTarget = nameof(VrTarget.Assignable);
    private string _src2SourceId = NoneSourceId;
    private string _src2VrTarget = nameof(VrTarget.Assignable);

    public ModuleMappingRowViewModel(int index, ObservableCollection<string> availableSourceIds)
    {
        Index = index;
        AvailableSourceIds = availableSourceIds;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public int Index { get; }

    /// <summary>Shared list of known source ids, prefixed with <see cref="NoneSourceId"/>.</summary>
    public ObservableCollection<string> AvailableSourceIds { get; }

    public IReadOnlyList<string> VrTargets { get; } = Enum.GetNames<VrTarget>();

    public string Src1SourceId
    {
        get => _src1SourceId;
        set { _src1SourceId = value; OnPropertyChanged(); }
    }

    public string Src1VrTarget
    {
        get => _src1VrTarget;
        set { _src1VrTarget = value; OnPropertyChanged(); }
    }

    public string Src2SourceId
    {
        get => _src2SourceId;
        set { _src2SourceId = value; OnPropertyChanged(); }
    }

    public string Src2VrTarget
    {
        get => _src2VrTarget;
        set { _src2VrTarget = value; OnPropertyChanged(); }
    }

    public ModuleMapping ToModuleMapping() => new(
        Index,
        new ModuleSourceBinding(Src1SourceId == NoneSourceId ? null : Src1SourceId, Src1VrTarget),
        new ModuleSourceBinding(Src2SourceId == NoneSourceId ? null : Src2SourceId, Src2VrTarget));

    public void LoadFrom(ModuleMapping mapping)
    {
        Src1SourceId = mapping.Src1.SourceId ?? NoneSourceId;
        Src1VrTarget = mapping.Src1.VrTarget;
        Src2SourceId = mapping.Src2.SourceId ?? NoneSourceId;
        Src2VrTarget = mapping.Src2.VrTarget;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
