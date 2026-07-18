using Microsoft.AspNetCore.Http;
using Switcher.Contracts;

namespace Switcher.Web.Endpoints;

/// <summary>
/// Reports SRT setup guidance for the local PC via <see cref="IDeviceQueryService"/>
/// (docs/specs/multiview-output-revision.md §4.1 / §2.7). SRT sources are push-based, so instead of
/// enumerating devices the caller obtains a listener port, host candidates and a ready-to-copy URL.
/// </summary>
internal static class SrtSetupEndpoint
{
    public static async Task<IResult> GetAsync(
        IDeviceQueryService deviceQueryService,
        CancellationToken cancellationToken)
    {
        var setup = await deviceQueryService.GetSrtSetupAsync(cancellationToken);
        return Results.Ok(setup);
    }
}
