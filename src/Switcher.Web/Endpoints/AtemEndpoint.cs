using Microsoft.AspNetCore.Http;
using Switcher.Contracts;

namespace Switcher.Web.Endpoints;

internal static class AtemEndpoint
{
    public static async Task<IResult> PutConfigAsync(
        AtemConfig? config,
        ISwitcherConfigService configService,
        CancellationToken cancellationToken)
    {
        if (config is null)
        {
            return Results.BadRequest(new { errors = new[] { "Request body is required." } });
        }

        await configService.ApplyAtemConfigAsync(config, cancellationToken);
        return Results.NoContent();
    }

    public static async Task<IResult> PostCommandAsync(
        AtemCommandRequest? command,
        ISwitcherConfigService configService,
        CancellationToken cancellationToken)
    {
        if (command is null)
        {
            return Results.BadRequest(new { errors = new[] { "Request body is required." } });
        }

        await configService.SendAtemCommandAsync(command, cancellationToken);
        return Results.NoContent();
    }
}
