using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Switcher.App.Rendering;

/// <summary>
/// Shows an element only while a count is zero — used for the "no sources yet" hints next to
/// collection-backed panels, so an empty bus strip / device list explains itself instead of
/// rendering as blank space.
/// </summary>
public sealed class ZeroToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int count && count == 0 ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
