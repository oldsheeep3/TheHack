using Microsoft.AspNetCore.Http;
using Switcher.Contracts;

namespace Switcher.Web.Endpoints;

internal static class ConfigEndpoint
{
    public static async Task<IResult> PostAsync(
        ConfigChangeRequest? request,
        ISwitcherConfigService configService,
        CancellationToken cancellationToken)
    {
        var errors = ConfigChangeRequestValidator.Validate(request);
        if (errors.Count > 0)
        {
            return Results.BadRequest(new { errors });
        }

        await configService.ApplyConfigAsync(request!, cancellationToken);
        return Results.NoContent();
    }
}
