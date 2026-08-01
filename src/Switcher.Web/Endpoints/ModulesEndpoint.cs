using Microsoft.AspNetCore.Http;
using Switcher.Contracts;

namespace Switcher.Web.Endpoints;

internal static class ModulesEndpoint
{
    /// <summary>The module bindings currently in force (the <c>PUT</c> replaces the whole list).</summary>
    public static async Task<IResult> GetAsync(
        ISwitcherConfigService configService,
        CancellationToken cancellationToken) =>
        Results.Ok(await configService.GetModulesAsync(cancellationToken));

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
