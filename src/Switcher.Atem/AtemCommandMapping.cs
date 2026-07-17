namespace Switcher.Atem;

/// <summary>
/// Target ATEM command for a mapped controller button. <see cref="Source"/> is only meaningful for
/// <see cref="AtemAction.ProgramInput"/>/<see cref="AtemAction.PreviewInput"/>; it is ignored for
/// <see cref="AtemAction.Cut"/>/<see cref="AtemAction.Auto"/>.
/// </summary>
public sealed record AtemCommandMapping(AtemAction Action, byte MixEffect = 0, ushort Source = 0);
