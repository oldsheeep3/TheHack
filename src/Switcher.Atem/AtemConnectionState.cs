namespace Switcher.Atem;

/// <summary>Connection lifecycle exposed via <see cref="AtemController.ConnectionStateChanged"/>.</summary>
public enum AtemConnectionState
{
    Disconnected,
    Connecting,
    Connected,
}
