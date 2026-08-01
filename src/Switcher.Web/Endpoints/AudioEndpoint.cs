using Microsoft.AspNetCore.Http;
using Switcher.Contracts;

namespace Switcher.Web.Endpoints;

/// <summary>
/// Audio endpoints: enumerate the machine's render endpoints, and route program buses to them.
/// Each bus is independent and may feed more than one device, which is why this is a table rather than
/// a single "monitoring device" setting.
/// </summary>
internal static class AudioEndpoint
{
    public static IResult GetDevices(IVideoEngine engine) => Results.Ok(engine.QueryAudioDevices());

    /// <summary>The routing table currently in force (the <c>PUT</c> replaces the whole table).</summary>
    public static async Task<IResult> GetOutputsAsync(
        ISwitcherConfigService configService,
        CancellationToken cancellationToken) =>
        Results.Ok(await configService.GetAudioOutputsAsync(cancellationToken));

    public static async Task<IResult> PutOutputsAsync(
        AudioOutputsRequest? request,
        ISwitcherConfigService configService,
        CancellationToken cancellationToken)
    {
        var errors = AudioOutputsRequestValidator.Validate(request);
        if (errors.Count > 0)
        {
            return Results.BadRequest(new { errors });
        }

        await configService.ApplyAudioOutputsAsync(request!, cancellationToken);
        return Results.NoContent();
    }
}
