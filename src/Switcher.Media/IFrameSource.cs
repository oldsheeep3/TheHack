using Switcher.Contracts;

namespace Switcher.Media;

/// <summary>
/// Internal seam between <see cref="InputSourceManager"/> (which owns decoded frames per channel)
/// and <see cref="CompositorEngine"/> (which needs to read them for GPU compositing), kept separate
/// from <see cref="IInputSourceManager"/> since that public contract only exposes source metadata.
/// </summary>
internal interface IFrameSource
{
    /// <summary>Returns the most recently decoded frame for <paramref name="channel"/>, if any.</summary>
    bool TryGetLatestFrame(int channel, out FrameData? frame);

    /// <summary>Resolves an OBS-like <see cref="Contracts.SourceDefinition.Id"/> to its stable integer
    /// channel/ordinal, so <see cref="CompositorEngine"/> can build layers (keyed by channel, per
    /// <see cref="Compositing.PipLayoutCalculator"/>) from a <see cref="Contracts.ProgramRequest"/>'s
    /// string source IDs.</summary>
    bool TryResolveChannel(string sourceId, out int channel);
}
