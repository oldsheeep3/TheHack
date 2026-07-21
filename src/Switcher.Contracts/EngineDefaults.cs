namespace Switcher.Contracts;

/// <summary>
/// Default canvas geometry for the video engine (docs/specs/libobs-engine-migration.md §2).
/// Mirrors the values the GStreamer-era compositor used so the dual-M/E defaults are unchanged
/// across the libobs migration.
/// </summary>
public static class EngineDefaults
{
    public const int CanvasWidth = 1920;

    public const int CanvasHeight = 1080;

    public const int Fps = 60;
}
