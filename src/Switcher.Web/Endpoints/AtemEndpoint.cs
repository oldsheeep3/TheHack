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

    public static async Task<IResult> GetConfigAsync(
        ISwitcherConfigService configService,
        CancellationToken cancellationToken) =>
        Results.Ok(await configService.GetAtemConfigAsync(cancellationToken));

    public static async Task<IResult> GetDevicesAsync(
        ISwitcherConfigService configService,
        CancellationToken cancellationToken) =>
        Results.Ok(await configService.DiscoverAtemDevicesAsync(cancellationToken));

    public static async Task<IResult> PostStreamingAsync(
        AtemStreamingRequest? request,
        ISwitcherConfigService configService,
        CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Url))
        {
            return Results.BadRequest(new { errors = new[] { "A streaming URL is required." } });
        }

        var applied = await configService.ConfigureAtemStreamingAsync(request, cancellationToken);

        // Not an error in the request - the switcher simply is not reachable right now - so this reports
        // "the resource is not in a state to accept it" rather than 4xx-blaming the caller's body.
        return applied
            ? Results.NoContent()
            : Results.Conflict(new { errors = new[] { "No ATEM is connected." } });
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
