namespace Switcher.App.Display;

/// <summary>
/// One selectable physical display for the HDMI-output / operator-move / multiview-full-screen pickers
/// (requirements 1/2/4). Enumerated from <c>System.Windows.Forms.Screen.AllScreens</c> by the windows
/// that own the pickers; kept as a plain record (no WinForms dependency) so it can be constructed in
/// tests and bound directly as a combo-box item.
/// </summary>
public sealed record DisplayOption(int Index, int Width, int Height, bool IsPrimary)
{
    /// <summary>Combo-box label, e.g. <c>"Display 0 — 1920x1080 (Primary)"</c>.</summary>
    public string Label =>
        $"Display {Index} — {Width}x{Height}{(IsPrimary ? " (Primary)" : string.Empty)}";
}
