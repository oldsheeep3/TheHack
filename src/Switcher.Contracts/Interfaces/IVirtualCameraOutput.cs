namespace Switcher.Contracts;

/// <summary>
/// Exposes the composited PGM output as a virtual camera device to the OS.
/// </summary>
public interface IVirtualCameraOutput
{
    void Start();

    void SubmitFrame(FrameData frame);

    void Stop();
}
