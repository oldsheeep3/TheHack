using Switcher.Contracts;

namespace Switcher.Web;

/// <summary>
/// Validates <see cref="SourceDefinition"/> payloads received on <c>POST /api/v1/sources</c> and
/// <c>PUT /api/v1/sources/{id}</c> before they are handed to <see cref="ISwitcherConfigService"/>.
/// </summary>
public static class SourceDefinitionValidator
{
    public static IReadOnlyList<string> Validate(SourceDefinition? source)
    {
        if (source is null)
        {
            return ["Request body is required."];
        }

        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(source.Id))
        {
            errors.Add("id is required.");
        }

        if (string.IsNullOrWhiteSpace(source.Name))
        {
            errors.Add("name is required.");
        }

        switch (source.Type)
        {
            case SourceType.Ndi:
                if (source.Ndi is null || string.IsNullOrWhiteSpace(source.Ndi.SourceName))
                {
                    errors.Add("ndi.source_name is required when type is NDI.");
                }

                break;
            case SourceType.Webcam:
                if (source.Webcam is null || string.IsNullOrWhiteSpace(source.Webcam.DeviceId))
                {
                    errors.Add("webcam.device_id is required when type is WEBCAM.");
                }

                break;
            case SourceType.Srt:
                if (source.Srt is null || string.IsNullOrWhiteSpace(source.Srt.Url))
                {
                    errors.Add("srt.url is required when type is SRT.");
                }

                break;
        }

        return errors;
    }
}
