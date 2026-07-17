namespace Switcher.Contracts;

/// <summary>
/// A single composited frame handed from the compositor to a frame consumer (e.g. the virtual camera output).
/// </summary>
public sealed record FrameData(int Width, int Height, ReadOnlyMemory<byte> Pixels);
