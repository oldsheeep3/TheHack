using Microsoft.AspNetCore.Http;
using Switcher.Contracts;

namespace Switcher.Web.Endpoints;

internal static class PicoNetworkEndpoint
{
    public static async Task<IResult> PutAsync(
        PicoNetworkConfig? config,
        ISwitcherConfigService configService,
        CancellationToken cancellationToken)
    {
        if (config is null)
        {
            return Results.BadRequest(new { errors = new[] { "Request body is required." } });
        }

        await configService.ApplyPicoNetworkConfigAsync(config, cancellationToken);
        return Results.NoContent();
    }
}
