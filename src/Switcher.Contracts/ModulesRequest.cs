namespace Switcher.Contracts;

/// <summary>
/// What a module's VR (potentiometer) input controls once bound to a source
/// (docs/specs/00-system-overview.md §4.2). Kept open-ended for future targets.
/// </summary>
public enum VrTarget
{
    Transition,
    Opacity,
    Assignable,
}

/// <summary>
/// One physical VR/switch pair binding on a module. VrTarget is a plain string (rather than the
/// <see cref="VrTarget"/> enum) so new target kinds don't require a contract change.
/// </summary>
public sealed record ModuleSourceBinding(string? SourceId, string VrTarget);

public sealed record ModuleMapping(int Index, ModuleSourceBinding Src1, ModuleSourceBinding Src2);

/// <summary>
/// Request body for PUT /api/v1/modules (docs/specs/00-system-overview.md §4.2).
/// </summary>
public sealed record ModulesRequest(IReadOnlyList<ModuleMapping> Modules);
