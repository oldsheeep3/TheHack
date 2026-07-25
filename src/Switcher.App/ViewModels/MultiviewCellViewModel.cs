using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media.Imaging;
using Switcher.Contracts;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;

namespace Switcher.App.ViewModels;

/// <summary>
/// One region of the configurable multiview (docs/specs/multiview-output-revision.md §2.3/§4.1): a
/// rectangular area (<see cref="Row"/>/<see cref="Col"/>/<see cref="RowSpan"/>/<see cref="ColSpan"/> on
/// the 4x4 grid) assigned to one of <c>PGM1</c>/<c>PGM2</c>/<c>PVW1</c>/<c>PVW2</c>/<c>SRC:&lt;id&gt;</c>/
/// <c>EMPTY</c> (the same token vocabulary as <see cref="Switcher.Contracts.MultiviewRegion"/>).
///
/// Framed red when its content is on air and green when it is staged — for <c>SRC:</c> regions as well
/// as the bus regions, so an input's tally is visible without tracing it through the bus strip. Shared
/// by the operator window's read-only multiview dock and the editor in
/// <c>MultiviewSettingsWindow</c>, where <see cref="Selected"/> also drives the merge/split drag
/// highlight.
/// </summary>
public sealed class MultiviewCellViewModel : INotifyPropertyChanged
{
    private string _token = "EMPTY";
    private BitmapSource? _image;
    private bool _selected;
    private CellTally _tally;
    private ProgramBus _tallyBus = ProgramBus.Pgm1;

    public MultiviewCellViewModel(
        int row,
        int col,
        int rowSpan,
        int colSpan,
        string token,
        ObservableCollection<string> availableTokens)
    {
        Row = row;
        Col = col;
        RowSpan = rowSpan;
        ColSpan = colSpan;
        _token = token;
        AvailableTokens = availableTokens;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public int Row { get; }

    public int Col { get; }

    public int RowSpan { get; }

    public int ColSpan { get; }

    /// <summary>Shared token list (kept up to date by the owning window as sources come and go), bound
    /// as the assignment combo box's <c>ItemsSource</c>.</summary>
    public ObservableCollection<string> AvailableTokens { get; }

    public string Token
    {
        get => _token;
        set
        {
            if (_token == value)
            {
                return;
            }

            _token = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Label));
            OnPropertyChanged(nameof(HasLabel));
            OnPropertyChanged(nameof(BorderBrush));
        }
    }

    public string Label => Token == "EMPTY" ? string.Empty : Token;

    /// <summary>Whether to draw the caption strip at all — an empty cell shows nothing rather than an
    /// empty black bar.</summary>
    public bool HasLabel => Token != "EMPTY";

    /// <summary>Whether this region is part of the current drag selection (requirement 3). Drawn with a
    /// distinct highlight border so the operator sees the rubber-banded rectangle.</summary>
    public bool Selected
    {
        get => _selected;
        set
        {
            if (_selected == value)
            {
                return;
            }

            _selected = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(BorderBrush));
        }
    }

    /// <summary>
    /// Whether this region's content is live on a program bus or staged on a preview bus. Set by the
    /// owning window from the orchestrator's bus membership. A <c>SRC:&lt;id&gt;</c> region is tallied
    /// exactly like the PGM/PVW regions are, so the operator can see at a glance which of their inputs
    /// is on air without tracing it through the bus strip.
    /// </summary>
    public CellTally Tally
    {
        get => _tally;
        set
        {
            if (_tally == value)
            {
                return;
            }

            _tally = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(BorderBrush));
            OnPropertyChanged(nameof(BorderThickness));
        }
    }

    public Brush BorderBrush => Selected
        ? Brushes.DeepSkyBlue
        : Tally switch
        {
            CellTally.Program => Switcher.App.Rendering.TallyPalette.ProgramBrush(TallyBus),
            CellTally.Preview => Switcher.App.Rendering.TallyPalette.PreviewBrush(TallyBus),
            _ => Token == "EMPTY" ? Brushes.Transparent : UntalliedBorder,
        };

    /// <summary>Tallied regions get a heavier frame so red/green reads before colour does — a monitor
    /// wall is scanned peripherally, and thickness survives that better than hue alone.</summary>
    public System.Windows.Thickness BorderThickness =>
        Selected || Tally != CellTally.None ? new System.Windows.Thickness(3) : new System.Windows.Thickness(1);

    /// <summary>Which bus's colour a tallied cell wears. A PGM2/PVW2 cell is coloured by bus 2; a
    /// SRC cell takes whichever bus it is live on, set by the owning window.</summary>
    public ProgramBus TallyBus
    {
        get => _tallyBus;
        set
        {
            if (_tallyBus == value)
            {
                return;
            }

            _tallyBus = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(BorderBrush));
        }
    }

    private static readonly Brush UntalliedBorder = CreateFrozen(0x33, 0x38, 0x3F);

    private static Brush CreateFrozen(byte r, byte g, byte b)
    {
        var brush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    public BitmapSource? Image
    {
        get => _image;
        set
        {
            _image = value;
            OnPropertyChanged();
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
