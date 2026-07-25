using Switcher.Contracts;

namespace Switcher.Web;

/// <summary>
/// Validates <see cref="AudioOutputsRequest"/> payloads for <c>PUT /api/v1/audio/outputs</c>.
/// <para>
/// An empty table is legal — it means "play nothing out", which is how an operator silences local
/// monitoring. Duplicate (bus, device) pairs are not, because two sinks on the same endpoint would
/// double up the same audio.
/// </para>
/// </summary>
public static class AudioOutputsRequestValidator
{
    public static IReadOnlyList<string> Validate(AudioOutputsRequest? request)
    {
        if (request is null)
        {
            return ["Request body is required."];
        }

        if (request.Outputs is null)
        {
            return ["outputs is required (use an empty array to route nothing)."];
        }

        var errors = new List<string>();
        var seen = new HashSet<(ProgramBus, string)>();

        for (var i = 0; i < request.Outputs.Count; i++)
        {
            var output = request.Outputs[i];

            if (!Enum.IsDefined(output.Bus))
            {
                errors.Add($"outputs[{i}].bus must be PGM1 or PGM2.");
            }

            // An empty device id is meaningful: it selects the system default endpoint.
            var key = (output.Bus, output.DeviceId ?? string.Empty);
            if (!seen.Add(key))
            {
                errors.Add($"outputs[{i}] repeats bus {output.Bus} on the same device.");
            }
        }

        return errors;
    }
}
