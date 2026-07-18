using Switcher.Contracts;

namespace Switcher.Web;

/// <summary>
/// Validates <see cref="ProgramRequest"/> payloads received on <c>POST /api/v1/program</c> before
/// they are handed to <see cref="ISwitcherConfigService"/>.
/// </summary>
public static class ProgramRequestValidator
{
    public static IReadOnlyList<string> Validate(ProgramRequest? request)
    {
        if (request is null)
        {
            return ["Request body is required."];
        }

        var errors = new List<string>();

        if (request.Layers is null)
        {
            errors.Add("layers is required.");
        }
        else
        {
            for (var i = 0; i < request.Layers.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(request.Layers[i].SourceId))
                {
                    errors.Add($"layers[{i}].source_id is required.");
                }
            }
        }

        return errors;
    }
}
