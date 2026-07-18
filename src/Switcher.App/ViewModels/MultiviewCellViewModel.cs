using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media.Imaging;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;

namespace Switcher.App.ViewModels;

/// <summary>
/// One cell of the 4x4 configurable multiview (docs/specs/pc-switcher-app.md §2.2/§5): a free
/// assignment to one of <c>PGM1</c>/<c>PGM2</c>/<c>PVW1</c>/<c>PVW2</c>/<c>SRC:&lt;id&gt;</c>/<c>EMPTY</c>
/// (the same token vocabulary as <see cref="Switcher.Contracts.MultiviewLayout"/>), rendered with a
/// red/green border for PGM/PVW cells per the operator console convention.
/// </summary>
public sealed class MultiviewCellViewModel : INotifyPropertyChanged
{
    private string _token = "EMPTY";
    private BitmapSource? _image;

    public MultiviewCellViewModel(int index, ObservableCollection<string> availableTokens)
    {
        Index = index;
        AvailableTokens = availableTokens;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public int Index { get; }

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

    public Brush BorderBrush => Token switch
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
