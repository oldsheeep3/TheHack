namespace Switcher.Contracts;

/// <summary>
/// Serial entry point for controller input events, queued from the Web layer into the app core.
/// </summary>
public interface IControllerInputSink
{
    void Enqueue(ButtonEvent buttonEvent);
}
