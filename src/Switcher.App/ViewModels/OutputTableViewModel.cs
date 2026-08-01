using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Switcher.App.Display;
using Switcher.Contracts;

namespace Switcher.App.ViewModels;

/// <summary>
/// The whole output-routing table as the Outputs dock edits it: the rows themselves plus the add-output
/// affordance and the ceilings it has to respect (docs/specs/00-system-overview.md §4.2).
/// <para>
/// This is deliberately a model rather than code inside <c>MainWindow</c>. The table is no longer a
/// fixed five rows the window can hard-code — the operator starts on
/// <see cref="OutputDefaults.Default"/> (one webcam, one display) and adds sinks up to
/// <see cref="OutputCatalog.MaxOf"/> of a kind and <see cref="OutputCatalog.MaxTotal"/> overall — so
/// "which sink does the next Add get", "may it be added at all" and "is this table valid" are decisions
/// with real edge cases at the ceilings. Keeping them here lets them be tested without a WPF window, the
/// same way <c>MultiviewRegionModel</c> holds the multiview editor's logic.
/// </para>
/// <para>
/// The per-kind ceiling is not the same for every kind: webcam stops at
/// <see cref="OutputCatalog.MaxWebcamSinks"/> (one) because OBS exposes a single virtual-camera output,
/// while HDMI and NDI go to <see cref="OutputCatalog.MaxSinksPerKind"/>. So the kind picker refuses a
/// second webcam while the table is still nowhere near full, and <see cref="AddRefusalReason"/> has to
/// say why in wording that reads correctly for a ceiling of one.
/// </para>
/// </summary>
public sealed class OutputTableViewModel : INotifyPropertyChanged
{
    private readonly ObservableCollection<DisplayOption> _availableDisplays;
    private OutputKindOption _selectedKind;

    public OutputTableViewModel(ObservableCollection<DisplayOption>? availableDisplays = null)
    {
        _availableDisplays = availableDisplays ?? [];
        KindOptions = [.. Enum.GetValues<OutputKind>().Select(k => new OutputKindOption(k))];
        _selectedKind = KindOptions[0];

        // Adding and removing rows moves the ceilings, the count and the refusal wording all at once.
        Rows.CollectionChanged += (_, _) => RaiseCapacityChanged();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>The live rows, bound as the routing <c>ItemsControl</c>'s <c>ItemsSource</c>.</summary>
    public ObservableCollection<OutputAssignmentRowViewModel> Rows { get; } = [];

    /// <summary>The kinds the operator can add, for the kind picker next to the Add button.</summary>
    public IReadOnlyList<OutputKindOption> KindOptions { get; }

    /// <summary>Which kind the Add button would add. Never null — a combo cleared by WPF (it blanks the
    /// selection while an <c>ItemsSource</c> is being replaced) keeps the previous choice rather than
    /// leaving Add with nothing to do.</summary>
    public OutputKindOption SelectedKind
    {
        get => _selectedKind;
        set
        {
            if (value is null)
            {
                // Snap the combo back rather than leaving it blank: the binding only re-reads the
                // property when it is told the value changed.
                OnPropertyChanged();
                return;
            }

            if (ReferenceEquals(value, _selectedKind))
            {
                return;
            }

            _selectedKind = value;
            OnPropertyChanged();
            RaiseCapacityChanged();
        }
    }

    /// <summary>The sinks the table currently holds, in row order.</summary>
    public IReadOnlyList<OutputSink> Sinks => [.. Rows.Select(r => r.Sink)];

    /// <summary>Whether <see cref="Add()"/> would produce a row. Bound to the Add button's
    /// <c>IsEnabled</c> so the ceilings are visible before they are hit rather than as a refusal after.</summary>
    public bool CanAdd => OutputCatalog.CanAdd(Sinks, SelectedKind.Kind);

    /// <summary>Unobtrusive "how full is the table" caption for the dock header, e.g. <c>"4/6 SINKS"</c>.</summary>
    public string Summary => $"{Rows.Count}/{OutputCatalog.MaxTotal} SINKS";

    /// <summary>Tooltip for the Add button: what it would add, or why it cannot. A disabled button with
    /// no explanation reads as a bug, and the two ceilings are hit for different reasons.</summary>
    public string AddHint => AddRefusalReason ?? $"Add a {SelectedKind.Label} output";

    /// <summary>Why a sink of <see cref="SelectedKind"/> cannot be added, or <c>null</c> when it can.
    /// The per-kind sentence is phrased off <see cref="OutputCatalog.MaxOf"/> rather than a constant, and
    /// has a singular form: webcam's ceiling is one, and "There are already 1 Webcam outputs" would read
    /// like a bug in the message rather than the limit it is reporting.</summary>
    public string? AddRefusalReason
    {
        get
        {
            if (CanAdd)
            {
                return null;
            }

            if (Rows.Count >= OutputCatalog.MaxTotal)
            {
                return $"The output table is full: {OutputCatalog.MaxTotal} sinks is the maximum.";
            }

            var max = OutputCatalog.MaxOf(SelectedKind.Kind);
            return max == 1
                ? $"Only {max} {SelectedKind.Label} output is supported."
                : $"There are already {max} {SelectedKind.Label} outputs, "
                  + "which is the maximum for one kind.";
        }
    }

    /// <summary>
    /// Rebuilds the rows from a persisted table — one row per assignment, grouped by kind and then by
    /// ordinal so the panel reads the same way on every start no matter what order the sinks were added
    /// or saved in. An empty (or absent) table means a fresh install, which starts on
    /// <see cref="OutputDefaults.Default"/>; a table that is over the ceilings is still loaded as-is, so
    /// the operator can see and fix what a hand-edited config left behind instead of having it silently
    /// discarded (<see cref="Validate"/> is what refuses to apply it).
    /// </summary>
    public void Load(IReadOnlyList<OutputAssignment>? assignments)
    {
        var source = assignments is { Count: > 0 } ? assignments : OutputDefaults.Default;

        Rows.Clear();
        foreach (var assignment in source
            .OrderBy(a => OutputCatalog.KindOf(a.Sink))
            .ThenBy(a => OutputCatalog.OrdinalOf(a.Sink)))
        {
            var row = new OutputAssignmentRowViewModel(assignment.Sink, _availableDisplays);
            row.LoadFrom(assignment);
            Rows.Add(row);
        }
    }

    /// <summary>Adds a sink of <see cref="SelectedKind"/>; see <see cref="Add(OutputKind)"/>.</summary>
    public OutputAssignmentRowViewModel? Add() => Add(SelectedKind.Kind);

    /// <summary>
    /// Adds the lowest free sink of <paramref name="kind"/>, or returns <c>null</c> when the per-kind or
    /// total ceiling refuses it. The new row starts on the bus that has no sink yet where there is one,
    /// so the common "PGM2 needs somewhere to go" case is one click rather than click-then-repick.
    /// </summary>
    public OutputAssignmentRowViewModel? Add(OutputKind kind)
    {
        if (OutputCatalog.NextAvailable(Sinks, kind) is not { } sink)
        {
            return null;
        }

        var row = new OutputAssignmentRowViewModel(sink, _availableDisplays);
        row.LoadFrom(OutputDefaults.NewAssignment(sink, SourceForNewSink()));
        Rows.Add(row);
        return row;
    }

    /// <summary>Drops a row. The freed ordinal is reused by the next <see cref="Add(OutputKind)"/> of
    /// that kind, so removing NDI 1 and adding an NDI back gives NDI 1 again.</summary>
    public bool Remove(OutputAssignmentRowViewModel row) => Rows.Remove(row);

    public OutputsRequest ToRequest() => new(ToAssignments());

    /// <summary>
    /// Everything wrong with the table as it stands, in the wording the Web API validator uses for the
    /// same faults (<see cref="OutputRules"/>): too many sinks, and program buses with nowhere to go.
    /// The Add button cannot produce an over-limit table, so an over-limit complaint here means a
    /// hand-edited config was loaded — which is exactly why applying re-checks rather than trusting the UI.
    /// </summary>
    public IReadOnlyList<string> Validate()
    {
        var assignments = ToAssignments();
        var errors = new List<string>(OutputRules.DescribeOverLimit(assignments));

        if (OutputRules.DescribeMissingBuses(OutputRules.MissingBuses(assignments)) is { } missing)
        {
            errors.Add(missing);
        }

        return errors;
    }

    private IReadOnlyList<OutputAssignment> ToAssignments() => [.. Rows.Select(r => r.ToOutputAssignment())];

    private OutputSource SourceForNewSink()
    {
        var missing = OutputRules.MissingBuses(ToAssignments());
        return missing.Count > 0 ? missing[0] : OutputSource.Pgm1;
    }

    private void RaiseCapacityChanged()
    {
        OnPropertyChanged(nameof(Sinks));
        OnPropertyChanged(nameof(CanAdd));
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(AddHint));
        OnPropertyChanged(nameof(AddRefusalReason));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

/// <summary>
/// A kind in the add-output picker. The combo needs an item with an operator-facing <see cref="Label"/>
/// ("Webcam", not the <c>Webcam</c>/<c>Hdmi</c>/<c>Ndi</c> enum spelling), and binding through a small
/// record keeps that out of a value converter — the same shape <see cref="DisplayOption"/> uses for the
/// display pickers.
/// </summary>
public sealed record OutputKindOption(OutputKind Kind)
{
    public string Label => OutputCatalog.LabelOf(Kind);
}
