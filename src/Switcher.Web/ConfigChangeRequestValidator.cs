using Switcher.Contracts;

namespace Switcher.Web;

/// <summary>
/// Validates <see cref="ConfigChangeRequest"/> payloads received on <c>POST /api/v1/config</c>
/// before they are handed to <see cref="ISwitcherConfigService"/>.
/// </summary>
public static class ConfigChangeRequestValidator
{
    public static IReadOnlyList<string> Validate(ConfigChangeRequest? request)
    {
        if (request is null)
        {
            return ["Request body is required."];
        }

        var errors = new List<string>();

        if (request.TargetChannel < 1)
        {
            errors.Add("target_channel must be >= 1.");
        }

        if (request.SourceType != SourceProtocol.Uvc && string.IsNullOrWhiteSpace(request.SourceUrl))
        {
            errors.Add("source_url is required for NDI/SRT sources.");
        }

        if (request.PipSettings is { } pip)
        {
            ValidatePipSettings(pip, errors);
        }

        return errors;
    }

    private static void ValidatePipSettings(PipSettings pip, List<string> errors)
    {
        if (pip.Width < 0)
        {
            errors.Add("pip_settings.width must be >= 0.");
        }

        if (pip.Height < 0)
        {
            errors.Add("pip_settings.height must be >= 0.");
        }

        if (pip.X < 0)
        {
            errors.Add("pip_settings.x_position must be >= 0.");
        }

        if (pip.Y < 0)
        {
            errors.Add("pip_settings.y_position must be >= 0.");
        }

        if (pip.Opacity is < 0.0 or > 1.0)
        {
            errors.Add("pip_settings.opacity must be between 0.0 and 1.0.");
        }

        if (pip.Crop is { } crop)
        {
            if (crop.Left < 0 || crop.Top < 0)
            {
                errors.Add("pip_settings.crop left/top must be >= 0.");
            }

            if (crop.Right <= crop.Left || crop.Bottom <= crop.Top)
            {
                errors.Add("pip_settings.crop must describe a positive-area rectangle (right > left, bottom > top).");
            }
        }
    }
}
