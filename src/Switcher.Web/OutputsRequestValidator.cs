using Switcher.Contracts;

namespace Switcher.Web;

/// <summary>
/// Validates <see cref="OutputsRequest"/> payloads received on <c>PUT /api/v1/outputs</c>
/// (docs/specs/00-system-overview.md §4.2): each sink assigned at most once, and an HDMI output
/// must specify which display to drive.
/// </summary>
public static class OutputsRequestValidator
{
    public static IReadOnlyList<string> Validate(OutputsRequest? request)
    {
        if (request is null)
        {
            return ["Request body is required."];
        }

        var errors = new List<string>();

        if (request.Outputs is null)
        {
            errors.Add("outputs is required.");
            return errors;
        }

        var seenSinks = new HashSet<OutputSink>();
        for (var i = 0; i < request.Outputs.Count; i++)
        {
            var output = request.Outputs[i];
            if (!seenSinks.Add(output.Sink))
            {
                errors.Add($"outputs[{i}].sink '{output.Sink}' is assigned more than once.");
            }

            if (output.Sink == OutputSink.Hdmi && output.DisplayId is null)
            {
                errors.Add($"outputs[{i}].display_id is required when sink is HDMI.");
            }
        }

        return errors;
    }
}
