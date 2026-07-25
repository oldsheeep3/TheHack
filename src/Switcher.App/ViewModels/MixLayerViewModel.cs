using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media.Imaging;
using Switcher.Contracts;

namespace Switcher.App.ViewModels;

/// <summary>
/// One member of a mix source while it is being edited: which source it shows and the rectangle it
/// occupies on the mix canvas, in canvas pixels (not screen pixels — the editor scales).
/// </summary>
public sealed class MixLayerViewModel : INotifyPropertyChanged
{
    private double _x;
    private double _y;
    private double _width;
    private double _height;
    private bool _selected;
    private BitmapSource? _image;

    public MixLayerViewModel(string sourceId, string name, MixLayer layer)
    {
        ArgumentNullException.ThrowIfNull(layer);
        SourceId = sourceId;
        Name = name;
        _x = layer.X;
        _y = layer.Y;
        _width = layer.Width;
        _height = layer.Height;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string SourceId { get; }

    public string Name { get; }

    /// <summary>Preview token for this layer's source, used to pull its thumbnail.</summary>
    public string Token => $"SRC:{SourceId}";

    public double X
    {
        get => _x;
        set => Set(ref _x, Math.Round(value));
    }

    public double Y
    {
        get => _y;
        set => Set(ref _y, Math.Round(value));
    }

    /// <summary>Kept at 16 px or more so a layer can never be dragged down to an unclickable sliver.</summary>
    public double Width
    {
        get => _width;
        set => Set(ref _width, Math.Max(16, Math.Round(value)));
    }

    public double Height
    {
        get => _height;
        set => Set(ref _height, Math.Max(16, Math.Round(value)));
    }

    public bool Selected
    {
        get => _selected;
        set => Set(ref _selected, value);
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

    public MixLayer ToLayer(int zOrder) =>
        new(SourceId, (int)X, (int)Y, (int)Width, (int)Height, zOrder, Crop: null);

    private void Set(ref double field, double value, [CallerMemberName] string? propertyName = null)
    {
        if (Math.Abs(field - value) < 0.5)
        {
            return;
        }

        field = value;
        OnPropertyChanged(propertyName);
    }

    private void Set(ref bool field, bool value, [CallerMemberName] string? propertyName = null)
    {
        if (field == value)
        {
            return;
        }

        field = value;
        OnPropertyChanged(propertyName);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
