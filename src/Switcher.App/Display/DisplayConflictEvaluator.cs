namespace Switcher.App.Display;

/// <summary>
/// UI-independent judgement for the "same-screen" warning shared by requirement 1 (HDMI full-screen
/// output) and requirement 4 (multiview full-screen), see docs/specs/multiview-output-revision.md §2.1/§2.4.
/// Kept free of any WPF/WinForms dependency so it can be unit-tested headlessly: it only compares the
/// operator console's display index (<see cref="Configuration.AppConfig.OperatorDisplayIndex"/>) against
/// the display a full-screen surface is about to occupy.
/// </summary>
public static class DisplayConflictEvaluator
{
    /// <summary>Returns <c>true</c> when a full-screen surface targeting <paramref name="targetDisplayId"/>
    /// would land on the same physical display as the operator console (<paramref name="operatorDisplayIndex"/>),
    /// which would hide the operator UI behind the full-screen output. A <c>null</c> target (no display
    /// configured yet) never conflicts.</summary>
    public static bool ConflictsWithOperator(int operatorDisplayIndex, int? targetDisplayId) =>
        targetDisplayId is { } id && id == operatorDisplayIndex;

    /// <summary>Human-readable warning body for the continue/cancel prompt (requirement 1/4). The prompt
    /// itself is non-blocking: the operator may proceed anyway.</summary>
    public static string BuildWarningMessage(int displayId) =>
        $"The selected full-screen output targets Display {displayId}, which is the same display as the " +
        "operator console. The operator UI will be hidden behind the full-screen output. Continue anyway?";
}
