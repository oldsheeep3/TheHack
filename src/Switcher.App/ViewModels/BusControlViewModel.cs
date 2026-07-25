using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Switcher.Contracts;

namespace Switcher.App.ViewModels;

/// <summary>
/// One source's staging state on a single bus, rendered as a toggle button in the bus strip.
/// <see cref="Staged"/> mirrors that bus's PVW membership; <see cref="OnProgram"/> only drives the
/// on-air tint so the operator can see, per button, what is already live on that bus.
/// </summary>
public sealed class BusSourceViewModel : INotifyPropertyChanged
{
    private bool _staged;
    private bool _onProgram;

    public BusSourceViewModel(string id, string name)
    {
        Id = id;
        Name = name;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Id { get; }

    public string Name { get; }

    /// <summary>Whether this source is part of the bus's staged (PVW) composition.</summary>
    public bool Staged
    {
        get => _staged;
        set
        {
            if (_staged == value)
            {
                return;
            }

            _staged = value;
            OnPropertyChanged();
        }
    }

    /// <summary>Whether this source is currently live on the bus's program output.</summary>
    public bool OnProgram
    {
        get => _onProgram;
        set
        {
            if (_onProgram == value)
            {
                return;
            }

            _onProgram = value;
            OnPropertyChanged();
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

/// <summary>
/// Operator controls for one M/E bus (PGM1/PVW1 or PGM2/PVW2, docs/specs/pc-switcher-app.md §2.1):
/// which sources are staged on PVW, what is currently live on PGM, and the CUT/AUTO take.
/// The window turns <see cref="StagedSourceIds"/> into a <see cref="ProgramRequest"/>; nothing here
/// touches the engine directly.
/// </summary>
public sealed class BusControlViewModel : INotifyPropertyChanged
{
    private string _programText = "-";
    private string _previewText = "-";

    public BusControlViewModel(ProgramBus bus)
    {
        Bus = bus;
        ProgramLabel = bus == ProgramBus.Pgm2 ? "PGM2" : "PGM1";
        PreviewLabel = bus == ProgramBus.Pgm2 ? "PVW2" : "PVW1";
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ProgramBus Bus { get; }

    public string ProgramLabel { get; }

    public string PreviewLabel { get; }

    public ObservableCollection<BusSourceViewModel> Sources { get; } = [];

    public string ProgramText
    {
        get => _programText;
        set
        {
            if (_programText == value)
            {
                return;
            }

            _programText = value;
            OnPropertyChanged();
        }
    }

    public string PreviewText
    {
        get => _previewText;
        set
        {
            if (_previewText == value)
            {
                return;
            }

            _previewText = value;
            OnPropertyChanged();
        }
    }

    /// <summary>The staged composition, bottom layer first (z-order follows this order).</summary>
    public IReadOnlyList<string> StagedSourceIds =>
        Sources.Where(s => s.Staged).Select(s => s.Id).ToList();

    /// <summary>Rebuilds the toggle list after a source add/remove, preserving each surviving source's
    /// staged state so re-enumerating inputs never silently drops a bus's staged composition.</summary>
    public void SyncSources(IReadOnlyList<(string Id, string Name)> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);

        var stagedBefore = Sources.Where(s => s.Staged).Select(s => s.Id).ToHashSet(StringComparer.Ordinal);
        var onProgramBefore = Sources.Where(s => s.OnProgram).Select(s => s.Id).ToHashSet(StringComparer.Ordinal);

        Sources.Clear();
        foreach (var (id, name) in sources)
        {
            Sources.Add(new BusSourceViewModel(id, name)
            {
                Staged = stagedBefore.Contains(id),
                OnProgram = onProgramBefore.Contains(id),
            });
        }
    }

    /// <summary>Replaces the staged set with the bus's authoritative PVW membership.</summary>
    public void SetStaged(IReadOnlySet<string> previewSourceIds)
    {
        ArgumentNullException.ThrowIfNull(previewSourceIds);
        foreach (var source in Sources)
        {
            source.Staged = previewSourceIds.Contains(source.Id);
        }
    }

    /// <summary>Marks which of the listed sources are live on this bus (drives the on-air tint).</summary>
    public void SetOnProgram(IReadOnlySet<string> programSourceIds)
    {
        ArgumentNullException.ThrowIfNull(programSourceIds);
        foreach (var source in Sources)
        {
            source.OnProgram = programSourceIds.Contains(source.Id);
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
