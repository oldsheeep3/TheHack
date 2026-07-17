namespace Switcher.Atem.Protocol;

/// <summary>
/// ATEM command block names (4-char ASCII identifiers), scoped to the subset this client emits:
/// program/preview input switching and cut/auto transitions (docs/tasks/agent-A-003-atem-control.md
/// step 1). Names sourced from the reverse-engineered command catalogue in
/// https://github.com/SteffeyDev/atem-connection
/// (src/commands/MixEffects/{ProgramInputCommand,PreviewInputCommand,CutCommand,AutoTransitionCommand}.ts).
/// </summary>
public static class AtemCommandNames
{
    public const string ProgramInput = "CPgI";
    public const string PreviewInput = "CPvI";
    public const string Cut = "DCut";
    public const string Auto = "DAut";
}
