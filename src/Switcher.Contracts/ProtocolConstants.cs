namespace Switcher.Contracts;

/// <summary>
/// Ports and addresses fixed by the common protocol (docs/specs/00-system-overview.md §4.4).
/// </summary>
public static class ProtocolConstants
{
    public const int WebPort = 8080;
    public const int SrtListenPort = 9000;
    public const int TallyBroadcastPort = 9999;
    public const int AtemPort = 9910;
    public const string TallyBroadcastAddress = "255.255.255.255";
}
