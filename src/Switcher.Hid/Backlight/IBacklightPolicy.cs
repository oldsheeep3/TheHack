using Switcher.Contracts;

namespace Switcher.Hid.Backlight;

/// <summary>Maps one switch's bus context to the color its backlight should show. Pluggable so the
/// color scheme (docs/specs/pc-switcher-app.md §2.6: "例: PGMオン=赤/PVW=緑/選択可=淡色/消灯") can be
/// replaced without touching <see cref="BacklightCalculator"/>.</summary>
public interface IBacklightPolicy
{
    BacklightColor Compute(BacklightSwitchContext context);
}
