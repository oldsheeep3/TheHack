using Switcher.Contracts;

namespace Switcher.Media.Compositing;

/// <summary>
/// GPU-facing half of <see cref="Switcher.Media.CompositorEngine"/>: turns an ordered layer list plus
/// each layer's decoded source frame into one composited output frame. Kept as its own abstraction so
/// unit tests can substitute a fake and cover <see cref="Switcher.Media.CompositorEngine"/>'s
/// PGM/PVW/TAKE logic without a real DirectX 11 device (see <see cref="DirectX11Compositor"/> for the
/// real implementation, and README.md for its runtime prerequisites).
/// </summary>
internal interface IGpuCompositor : IDisposable
{
    FrameData Compose(IReadOnlyList<CompositedLayer> layers, Func<int, FrameData?> frameLookup, int canvasWidth, int canvasHeight);
}
