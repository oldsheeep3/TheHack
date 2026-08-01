using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using Switcher.Contracts;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace Switcher.App;

/// <summary>One editable tally colour. R/G/B are edited as text because these values go straight to the
/// module LEDs, and an operator matching a physical LED usually has the numbers, not a colour picker.</summary>
public sealed class TallyColorSlotViewModel : INotifyPropertyChanged
{
    private byte _r, _g, _b;

    public TallyColorSlotViewModel(string label, string hint, BacklightColor color)
    {
        Label = label;
        Hint = hint;
        (_r, _g, _b) = (color.R, color.G, color.B);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Label { get; }

    public string Hint { get; }

    public string R { get => _r.ToString(CultureInfo.InvariantCulture); set => SetChannel(ref _r, value); }

    public string G { get => _g.ToString(CultureInfo.InvariantCulture); set => SetChannel(ref _g, value); }

    public string B { get => _b.ToString(CultureInfo.InvariantCulture); set => SetChannel(ref _b, value); }

    public Brush Preview
    {
        get
        {
            var brush = new SolidColorBrush(Color.FromRgb(_r, _g, _b));
            brush.Freeze();
            return brush;
        }
    }

    public BacklightColor ToColor() => new(_r, _g, _b);

    public void Set(BacklightColor color)
    {
        (_r, _g, _b) = (color.R, color.G, color.B);
        OnPropertyChanged(nameof(R));
        OnPropertyChanged(nameof(G));
        OnPropertyChanged(nameof(B));
        OnPropertyChanged(nameof(Preview));
    }

    private void SetChannel(ref byte field, string text, [CallerMemberName] string? propertyName = null)
    {
        // Ignore anything that isn't a byte rather than throwing mid-typing; the box keeps the last
        // valid value and the swatch stays in sync with what will actually be sent.
        if (!byte.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) || field == value)
        {
            return;
        }

        field = value;
        OnPropertyChanged(propertyName);
        OnPropertyChanged(nameof(Preview));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

/// <summary>
/// Editor for the tally palette. One definition drives the operator window, the multiview frames and the
/// RGB LEDs on the physical modules, so this is the only place these colours are set.
/// </summary>
public partial class TallyColorsWindow : Window
{
    private readonly ObservableCollection<TallyColorSlotViewModel> _slots;

    public TallyColorsWindow(TallyColors colors)
    {
        ArgumentNullException.ThrowIfNull(colors);

        InitializeComponent();

        _slots =
        [
            new TallyColorSlotViewModel("PGM 1", "Live on program bus 1", colors.Pgm1),
            new TallyColorSlotViewModel("PGM 2", "Live on program bus 2", colors.Pgm2),
            new TallyColorSlotViewModel("PVW 1", "Staged on preview 1", colors.Pvw1),
            new TallyColorSlotViewModel("PVW 2", "Staged on preview 2", colors.Pvw2),
            new TallyColorSlotViewModel("Idle", "Bound to a source, not on air", colors.Idle),
        ];

        SlotsControl.ItemsSource = _slots;
    }

    /// <summary>The palette the operator applied; set only when the dialog closes with <c>true</c>.</summary>
    public TallyColors? Result { get; private set; }

    private void OnPickClick(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { Tag: TallyColorSlotViewModel slot })
        {
            return;
        }

        var current = slot.ToColor();
        using var dialog = new System.Windows.Forms.ColorDialog
        {
            FullOpen = true,
            Color = System.Drawing.Color.FromArgb(current.R, current.G, current.B),
        };

        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            slot.Set(new BacklightColor(dialog.Color.R, dialog.Color.G, dialog.Color.B));
        }
    }

    private void OnResetClick(object sender, RoutedEventArgs e)
    {
        var defaults = TallyColors.CreateDefault();
        _slots[0].Set(defaults.Pgm1);
        _slots[1].Set(defaults.Pgm2);
        _slots[2].Set(defaults.Pvw1);
        _slots[3].Set(defaults.Pvw2);
        _slots[4].Set(defaults.Idle);
    }

    private void OnApplyClick(object sender, RoutedEventArgs e)
    {
        Result = new TallyColors(
            _slots[0].ToColor(),
            _slots[1].ToColor(),
            _slots[2].ToColor(),
            _slots[3].ToColor(),
            _slots[4].ToColor());

        DialogResult = true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;
}
