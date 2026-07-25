using System.Globalization;
using System.Windows.Data;
using Switcher.Contracts;

namespace Switcher.App.Rendering;

/// <summary>
/// Renders <see cref="SourceAudioMode"/> in the wording used on hardware switchers — AFV / ON / OFF —
/// rather than the C# casing, so the control reads the way an operator expects it to.
/// </summary>
public sealed class AudioModeLabelConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is SourceAudioMode mode
            ? mode switch
            {
                SourceAudioMode.On => "ON",
                SourceAudioMode.Off => "OFF",
                _ => "AFV",
            }
            : string.Empty;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
