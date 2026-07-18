using Microsoft.AspNetCore.Http;
using Switcher.Contracts;

namespace Switcher.Web.Endpoints;

internal static class ModulesEndpoint
{
    public static async Task<IResult> PutAsync(
        ModulesRequest? request,
        ISwitcherConfigService configService,
        CancellationToken cancellationToken)
    {
        var errors = ModulesRequestValidator.Validate(request);
        if (errors.Count > 0)
        {
            return Results.BadRequest(new { errors });
        }

        await configService.ApplyModulesAsync(request!, cancellationToken);
        return Results.NoContent();
    }
}
