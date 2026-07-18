using Switcher.Contracts;

namespace Switcher.Web;

/// <summary>
/// Validates <see cref="ModulesRequest"/> payloads received on <c>PUT /api/v1/modules</c>: each
/// module index must be within <see cref="ProtocolConstants.MaxModules"/> and assigned at most once.
/// </summary>
public static class ModulesRequestValidator
{
    public static IReadOnlyList<string> Validate(ModulesRequest? request)
    {
        if (request is null)
        {
            return ["Request body is required."];
        }

        var errors = new List<string>();

        if (request.Modules is null)
        {
            errors.Add("modules is required.");
            return errors;
        }

        var seenIndices = new HashSet<int>();
        for (var i = 0; i < request.Modules.Count; i++)
        {
            var index = request.Modules[i].Index;
            if (index < 0 || index >= ProtocolConstants.MaxModules)
            {
                errors.Add($"modules[{i}].index must be between 0 and {ProtocolConstants.MaxModules - 1}.");
            }
            else if (!seenIndices.Add(index))
            {
                errors.Add($"modules[{i}].index {index} is assigned more than once.");
            }
        }

        return errors;
    }
}
