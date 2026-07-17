namespace Switcher.Contracts;

/// <summary>
/// ATEM remote control client entry point; translates controller input into ATEM commands.
/// </summary>
public interface IAtemController
{
    void Connect(string ip);

    void SendCommand(ButtonEvent buttonEvent);
}
