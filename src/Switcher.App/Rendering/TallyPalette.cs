using Switcher.Contracts;
using Application = System.Windows.Application;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace Switcher.App.Rendering;

/// <summary>
/// Publishes the operator's <see cref="TallyColors"/> as application resources, so every red/green in
/// the console — multiview cell frames, the program/preview monitors, bus buttons — comes from the same
/// definition the module LEDs are lit with.
///
/// The brushes are written into <c>Application.Current.Resources</c> under fixed keys and consumed with
/// <c>DynamicResource</c>, which means changing the palette restyles the whole window without anything
/// having to subscribe or re-render by hand.
/// </summary>
public static class TallyPalette
{
    public const string Pgm1Key = "TallyPgm1";
    public const string Pgm2Key = "TallyPgm2";
    public const string Pvw1Key = "TallyPvw1";
    public const string Pvw2Key = "TallyPvw2";
    public const string IdleKey = "TallyIdle";

    /// <summary>Bus-1 program colour, kept as the generic "on air" brush for surfaces that aren't tied
    /// to a particular bus (a source tile's status stripe, the legend).</summary>
    public const string ProgramKey = "TallyProgram";

    public const string PreviewKey = "TallyPreview";

    /// <summary>Writes <paramref name="colors"/> into the application resources. Call on the UI thread.</summary>
    public static void Apply(TallyColors colors)
    {
        ArgumentNullException.ThrowIfNull(colors);

        var resources = Application.Current?.Resources;
        if (resources is null)
        {
            return;  // design-time / headless
        }

        resources[Pgm1Key] = Freeze(colors.Pgm1);
        resources[Pgm2Key] = Freeze(colors.Pgm2);
        resources[Pvw1Key] = Freeze(colors.Pvw1);
        resources[Pvw2Key] = Freeze(colors.Pvw2);
        resources[IdleKey] = Freeze(colors.Idle);
        resources[ProgramKey] = Freeze(colors.Pgm1);
        resources[PreviewKey] = Freeze(colors.Pvw1);
    }

    /// <summary>The brush for a bus's program state, for code that builds brushes itself.</summary>
    public static Brush ProgramBrush(ProgramBus bus) =>
        Lookup(bus == ProgramBus.Pgm2 ? Pgm2Key : Pgm1Key);

    public static Brush PreviewBrush(ProgramBus bus) =>
        Lookup(bus == ProgramBus.Pgm2 ? Pvw2Key : Pvw1Key);

    private static Brush Lookup(string key) =>
        Application.Current?.TryFindResource(key) as Brush ?? System.Windows.Media.Brushes.Gray;

    private static SolidColorBrush Freeze(BacklightColor color)
    {
        var brush = new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B));
        brush.Freeze();
        return brush;
    }
}
