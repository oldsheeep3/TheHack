namespace Switcher.Atem.Protocol;

/// <summary>
/// ATEM command block names (4-char ASCII identifiers), scoped to the subset this client emits or
/// reads: program/preview input switching and cut/auto transitions (docs/tasks/agent-A-003-atem-control.md
/// step 1), plus the device-identification and streaming-output commands the ATEM picker and the
/// "ON AIR into an SRT source" helper need. Names sourced from the reverse-engineered command
/// catalogue in https://github.com/SteffeyDev/atem-connection
/// (src/commands/MixEffects/{ProgramInputCommand,PreviewInputCommand,CutCommand,AutoTransitionCommand}.ts,
/// src/commands/DeviceProfile/ProductIdentifierCommand.ts, src/commands/Streaming/*.ts).
/// </summary>
public static class AtemCommandNames
{
    public const string ProgramInput = "CPgI";
    public const string PreviewInput = "CPvI";
    public const string Cut = "DCut";
    public const string Auto = "DAut";

    /// <summary>Inbound: product identifier ("ATEM Mini Pro" etc.), part of the post-handshake state dump.</summary>
    public const string ProductIdentifier = "_pin";

    /// <summary>Outbound: set the streaming service name / URL / key.</summary>
    public const string SetStreamingService = "CRSS";

    /// <summary>Outbound: start or stop streaming.</summary>
    public const string SetStreamingState = "StrR";

    /// <summary>Inbound: current streaming state.</summary>
    public const string StreamingStatus = "StRS";
}
