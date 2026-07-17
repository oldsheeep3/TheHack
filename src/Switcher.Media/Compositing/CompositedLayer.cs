using Switcher.Contracts;

namespace Switcher.Media.Compositing;

/// <summary>One input channel's placement within a composited scene, in draw order (see <see cref="PipLayoutCalculator"/>).</summary>
internal sealed record CompositedLayer(int Channel, PipSettings Settings);
