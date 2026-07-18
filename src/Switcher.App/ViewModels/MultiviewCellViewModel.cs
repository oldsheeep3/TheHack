using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media.Imaging;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;

namespace Switcher.App.ViewModels;

/// <summary>
/// One region of the configurable multiview (docs/specs/multiview-output-revision.md §2.3/§4.1): a
/// rectangular area (<see cref="Row"/>/<see cref="Col"/>/<see cref="RowSpan"/>/<see cref="ColSpan"/> on
/// the 4x4 grid) freely assigned to one of <c>PGM1</c>/<c>PGM2</c>/<c>PVW1</c>/<c>PVW2</c>/<c>SRC:&lt;id&gt;</c>/
/// <c>EMPTY</c> (the same token vocabulary as <see cref="Switcher.Contracts.MultiviewRegion"/>), rendered
/// with a red/green border for PGM/PVW regions and a highlight while selected for a merge/split drag
/// (requirement 3).
/// </summary>
public sealed class MultiviewCellViewModel : INotifyPropertyChanged
{
    private string _token = "EMPTY";
    private BitmapSource? _image;
    private bool _selected;

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
            OnPropertyChanged(nameof(BorderBrush));
        }
    }

    public string Label => Token == "EMPTY" ? string.Empty : Token;

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

    public Brush BorderBrush => Selected
        ? Brushes.DeepSkyBlue
        : Token switch
        {
            "PGM1" or "PGM2" => Brushes.Crimson,
            "PVW1" or "PVW2" => Brushes.LimeGreen,
            "EMPTY" => Brushes.Transparent,
            _ => Brushes.SlateGray,
        };

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
