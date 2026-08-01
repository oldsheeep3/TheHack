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
            case SourceType.Image:
                if (source.Image is null || string.IsNullOrWhiteSpace(source.Image.FilePath))
                {
                    errors.Add("image.file_path is required when type is IMAGE.");
                }

                break;
            case SourceType.Html:
                if (source.Html is null || string.IsNullOrWhiteSpace(source.Html.Url))
                {
                    errors.Add("html.url is required when type is HTML.");
                }
                else
                {
                    if (source.Html.Width <= 0 || source.Html.Height <= 0)
                    {
                        errors.Add("html.width and html.height must be positive.");
                    }

                    if (source.Html.Fps <= 0)
                    {
                        errors.Add("html.fps must be positive.");
                    }
                }

                break;
            case SourceType.Mix:
                ValidateMix(source, errors);
                break;
        }

        return errors;
    }

    private static void ValidateMix(SourceDefinition source, List<string> errors)
    {
        if (source.Mix is not { } mix)
        {
            errors.Add("mix is required when type is MIX.");
            return;
        }

        if (mix.CanvasWidth <= 0 || mix.CanvasHeight <= 0)
        {
            errors.Add("mix.canvas_width and mix.canvas_height must be positive.");
        }

        if (mix.Layers is null || mix.Layers.Count == 0)
        {
            errors.Add("mix.layers must contain at least one layer.");
            return;
        }

        for (var i = 0; i < mix.Layers.Count; i++)
        {
            var layer = mix.Layers[i];

            if (string.IsNullOrWhiteSpace(layer.SourceId))
            {
                errors.Add($"mix.layers[{i}].source_id is required.");
            }
            else if (string.Equals(layer.SourceId, source.Id, StringComparison.Ordinal))
            {
                // A mix is a scene; containing itself would be an infinite recursion the engine has to
                // reject anyway, so refuse it at the edge with a message that says why.
                errors.Add($"mix.layers[{i}].source_id refers to the mix itself.");
            }

            if (layer.Width <= 0 || layer.Height <= 0)
            {
                errors.Add($"mix.layers[{i}] must have a positive width and height.");
            }
        }
    }
}
