using Microsoft.AspNetCore.Http;
using Switcher.Contracts;

namespace Switcher.Web.Endpoints;

internal static class MultiviewEndpoint
{
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
