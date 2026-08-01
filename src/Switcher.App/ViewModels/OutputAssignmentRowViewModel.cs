using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Switcher.App.Display;
using Switcher.Contracts;

namespace Switcher.App.ViewModels;

/// <summary>
/// One row of the output-assignment panel: a physical sink and which PGM bus currently feeds it
/// (docs/specs/00-system-overview.md §4.2 <c>PUT /api/v1/outputs</c>).
/// <para>
/// The rows used to be a fixed five, one per sink that existed, so a row could name its own sink in a
/// literal. Since the table became operator-editable (<see cref="OutputCatalog"/>) a row can carry any
/// sink of any kind, in any order, so everything that used to be hard-coded per row — the header text
/// and which extra controls apply — is derived from <see cref="OutputCatalog"/> instead.
/// <see cref="DisplayId"/>/<see cref="HideCursor"/>/<see cref="Fullscreen"/> only apply to
/// <see cref="OutputKind.Hdmi"/> rows (requirement 1); <see cref="NdiName"/> only to
/// <see cref="OutputKind.Ndi"/> rows (requirement 5).
/// </para>
/// </summary>
public sealed class OutputAssignmentRowViewModel : INotifyPropertyChanged
{
    private OutputSource _source;
    private int _displayId;
    private bool _hideCursor = true;
    private bool _fullscreen = true;
    private string _ndiName = string.Empty;

    public OutputAssignmentRowViewModel(OutputSink sink, ObservableCollection<DisplayOption>? availableDisplays = null)
    {
        Sink = sink;
        AvailableDisplays = availableDisplays ?? [];
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public OutputSink Sink { get; }

    public OutputKind Kind => OutputCatalog.KindOf(Sink);

    /// <summary>Row header, e.g. <c>"HDMI 2"</c>. The rows are no longer in a known fixed order, so the
    /// operator needs the ordinal spelled out to tell one sink of a kind from another; the raw enum name
    /// (<c>Hdmi2</c>) is a wire detail rather than something to put in front of them.</summary>
    public string Label => $"{OutputCatalog.LabelOf(Kind)} {OutputCatalog.OrdinalOf(Sink)}";

    public bool IsHdmi => Kind == OutputKind.Hdmi;

    public bool IsNdi => Kind == OutputKind.Ndi;

    public IReadOnlyList<OutputSource> SourceOptions { get; } = Enum.GetValues<OutputSource>();

    /// <summary>Shared, live list of displays (requirement 1's HDMI display picker), bound as the row's
    /// display combo <c>ItemsSource</c> and kept up to date by the owning window.</summary>
    public ObservableCollection<DisplayOption> AvailableDisplays { get; }

    public OutputSource Source
    {
        get => _source;
        set { _source = value; OnPropertyChanged(); }
    }

    public int DisplayId
    {
        get => _displayId;
        set { _displayId = value; OnPropertyChanged(); OnPropertyChanged(nameof(SelectedDisplay)); }
    }

    /// <summary>The <see cref="DisplayOption"/> currently matching <see cref="DisplayId"/>, or
    /// <c>null</c>. Two-way bound by the HDMI display combo.</summary>
    public DisplayOption? SelectedDisplay
    {
        get => AvailableDisplays.FirstOrDefault(d => d.Index == _displayId);
        set { if (value is not null) { DisplayId = value.Index; } }
    }

    public bool HideCursor
    {
        get => _hideCursor;
        set { _hideCursor = value; OnPropertyChanged(); }
    }

    public bool Fullscreen
    {
        get => _fullscreen;
        set { _fullscreen = value; OnPropertyChanged(); }
    }

    public string NdiName
    {
        get => _ndiName;
        set { _ndiName = value; OnPropertyChanged(); }
    }

    public OutputAssignment ToOutputAssignment() => new(
        Sink,
        Source,
        DisplayId: IsHdmi ? DisplayId : null,
        HideCursor: IsHdmi ? HideCursor : null,
        Fullscreen: IsHdmi ? Fullscreen : null,
        NdiName: IsNdi ? (string.IsNullOrWhiteSpace(NdiName) ? null : NdiName) : null);

    public void LoadFrom(OutputAssignment assignment)
    {
        ArgumentNullException.ThrowIfNull(assignment);

        Source = assignment.Source;
        DisplayId = assignment.DisplayId ?? OutputDefaults.DefaultHdmiDisplayId;
        HideCursor = assignment.HideCursor ?? true;
        Fullscreen = assignment.Fullscreen ?? true;
        NdiName = assignment.NdiName ?? string.Empty;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
