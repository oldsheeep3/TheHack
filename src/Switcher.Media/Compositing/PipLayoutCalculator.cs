using Switcher.Contracts;

namespace Switcher.Media.Compositing;

/// <summary>
/// Turns the per-channel <see cref="PipSettings"/> of a scene into an ordered draw list: disabled
/// channels are dropped, and the rest are sorted back-to-front by <see cref="PipSettings.ZOrder"/> so
/// higher Z values paint over lower ones. Pure/deterministic so it can be unit tested without a GPU
/// (docs/tasks/agent-A-002-media-engine.md: "レイアウト計算（座標/クロップ/Zオーダー順序）").
/// </summary>
internal static class PipLayoutCalculator
{
    public static IReadOnlyList<CompositedLayer> BuildScene(IReadOnlyDictionary<int, PipSettings> settingsByChannel) =>
        settingsByChannel
            .Where(entry => entry.Value.Enabled)
            .OrderBy(entry => entry.Value.ZOrder)
            .ThenBy(entry => entry.Key)
            .Select(entry => new CompositedLayer(entry.Key, Clamp(entry.Value)))
            .ToList();

    private static PipSettings Clamp(PipSettings settings) =>
        settings with { Opacity = Math.Clamp(settings.Opacity, 0.0, 1.0) };
}
