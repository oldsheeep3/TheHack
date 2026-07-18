using Switcher.Contracts;

namespace Switcher.Web;

/// <summary>
/// Validates <see cref="OutputsRequest"/> payloads received on <c>PUT /api/v1/outputs</c>
/// (docs/specs/00-system-overview.md §4.2, docs/specs/multiview-output-revision.md §4.2): each sink
/// assigned at most once, an HDMI output must specify which display to drive, and NDI outputs may only
/// carry a program bus source (PGM1/PGM2) with a non-empty <c>ndi_name</c> when one is supplied
/// (a null <c>ndi_name</c> is allowed and defaulted by the App).
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

            if (output.Sink is OutputSink.Ndi1 or OutputSink.Ndi2)
            {
                if (output.Source is not (OutputSource.Pgm1 or OutputSource.Pgm2))
                {
                    errors.Add($"outputs[{i}].source '{output.Source}' is invalid for an NDI sink; expected PGM1 or PGM2.");
                }

                if (output.NdiName is not null && output.NdiName.Length == 0)
                {
                    errors.Add($"outputs[{i}].ndi_name must not be empty (omit it to use the default name).");
                }
            }
        }

        return errors;
    }
}
