namespace Switcher.Contracts;

/// <summary>
/// Composites PGM/PVW outputs from input sources, applies PiP layout, and handles TAKE.
/// </summary>
public interface ICompositorEngine
{
    void ApplyPipSettings(int channel, PipSettings settings);

    void Take();

    FrameData GetProgramFrame();

    FrameData GetPreviewFrame();
}
