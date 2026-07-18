using System.ComponentModel;
using System.Runtime.CompilerServices;
using Switcher.Contracts;

namespace Switcher.App.ViewModels;

/// <summary>
/// One row of the output-assignment panel: a fixed physical sink (<see cref="OutputSink.Vcam1"/>/
/// <see cref="OutputSink.Vcam2"/>/<see cref="OutputSink.Hdmi"/>) and which PGM bus currently feeds it
/// (docs/specs/00-system-overview.md §4.2 <c>PUT /api/v1/outputs</c>). <see cref="DisplayId"/>/
/// <see cref="HideCursor"/>/<see cref="Fullscreen"/> only apply to the <see cref="OutputSink.Hdmi"/> row.
/// </summary>
public sealed class OutputAssignmentRowViewModel : INotifyPropertyChanged
{
    private OutputSource _source;
    private int _displayId;
    private bool _hideCursor = true;
    private bool _fullscreen = true;

    public OutputAssignmentRowViewModel(OutputSink sink) => Sink = sink;

    public event PropertyChangedEventHandler? PropertyChanged;

    public OutputSink Sink { get; }

    public bool IsHdmi => Sink == OutputSink.Hdmi;

    public IReadOnlyList<OutputSource> SourceOptions { get; } = Enum.GetValues<OutputSource>();

    public OutputSource Source
    {
        get => _source;
        set { _source = value; OnPropertyChanged(); }
    }

    public int DisplayId
    {
        get => _displayId;
        set { _displayId = value; OnPropertyChanged(); }
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

    public OutputAssignment ToOutputAssignment() => new(
        Sink,
        Source,
        DisplayId: IsHdmi ? DisplayId : null,
        HideCursor: IsHdmi ? HideCursor : null,
        Fullscreen: IsHdmi ? Fullscreen : null);

    public void LoadFrom(OutputAssignment assignment)
    {
        Source = assignment.Source;
        DisplayId = assignment.DisplayId ?? 0;
        HideCursor = assignment.HideCursor ?? true;
        Fullscreen = assignment.Fullscreen ?? true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
