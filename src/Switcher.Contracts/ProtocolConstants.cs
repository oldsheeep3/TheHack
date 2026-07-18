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

    // §4.0 system constants
    public const int MaxModules = 8;
    public const int SwitchesPerModule = 4;
    public const int VrsPerModule = 2;
    public const int BacklightsPerModule = 4;
    public const int ProgramBusCount = 2;

    // §4.1 HID report IDs
    public const byte HidInputReportId = 0x01;
    public const byte HidOutputReportId = 0x02;
}
