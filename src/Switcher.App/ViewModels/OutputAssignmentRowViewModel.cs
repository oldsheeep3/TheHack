using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Switcher.App.Display;
using Switcher.Contracts;

namespace Switcher.App.ViewModels;

/// <summary>
/// One row of the output-assignment panel: a fixed physical sink (<see cref="OutputSink.Vcam1"/>/
/// <see cref="OutputSink.Vcam2"/>/<see cref="OutputSink.Hdmi"/>/<see cref="OutputSink.Ndi1"/>/
/// <see cref="OutputSink.Ndi2"/>) and which PGM bus currently feeds it (docs/specs/00-system-overview.md
/// §4.2 <c>PUT /api/v1/outputs</c>). <see cref="DisplayId"/>/<see cref="HideCursor"/>/
/// <see cref="Fullscreen"/> only apply to the <see cref="OutputSink.Hdmi"/> row (requirement 1);
/// <see cref="NdiName"/> only applies to the NDI rows (requirement 5).
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

    public bool IsHdmi => Sink == OutputSink.Hdmi;

    public bool IsNdi => Sink is OutputSink.Ndi1 or OutputSink.Ndi2;

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
        Source = assignment.Source;
        DisplayId = assignment.DisplayId ?? 0;
        HideCursor = assignment.HideCursor ?? true;
        Fullscreen = assignment.Fullscreen ?? true;
        NdiName = assignment.NdiName ?? string.Empty;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
