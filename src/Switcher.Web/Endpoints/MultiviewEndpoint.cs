using Microsoft.AspNetCore.Http;
using Switcher.Contracts;

namespace Switcher.Web.Endpoints;

internal static class MultiviewEndpoint
{
    /// <summary>The layout currently in force, so a settings client edits it rather than replacing it
    /// with whatever its own blank grid happened to hold.</summary>
    public static async Task<IResult> GetAsync(
        ISwitcherConfigService configService,
        CancellationToken cancellationToken) =>
        Results.Ok(await configService.GetMultiviewLayoutAsync(cancellationToken));

    public static async Task<IResult> PutAsync(
        MultiviewLayout? layout,
        ISwitcherConfigService configService,
        CancellationToken cancellationToken)
    {
        var errors = MultiviewLayoutValidator.Validate(layout);
        if (errors.Count > 0)
        {
            return Results.BadRequest(new { errors });
        }

        await configService.ApplyMultiviewAsync(layout!, cancellationToken);
        return Results.NoContent();
    }
}
