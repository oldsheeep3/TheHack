using Microsoft.AspNetCore.Http;
using Switcher.Contracts;

namespace Switcher.Web.Endpoints;

/// <summary>
/// Enumerates input devices for the setup UI via <see cref="IDeviceQueryService"/>
/// (docs/specs/multiview-output-revision.md §4.1). Only <c>webcam</c> and <c>ndi</c> are enumerable;
/// SRT is push-based and therefore returns 400 (use <c>GET /api/v1/srt/setup</c> instead). An empty
/// result is a normal 200 with an empty array (the App renders the "no devices" guidance).
/// </summary>
internal static class DevicesEndpoint
{
    public static async Task<IResult> GetAsync(
        string type,
        IDeviceQueryService deviceQueryService,
        CancellationToken cancellationToken)
    {
        if (!TryParseType(type, out var queryType))
        {
            return Results.BadRequest(new
            {
                errors = new[] { $"Unknown or non-enumerable device type '{type}'. Expected 'webcam' or 'ndi'." },
            });
        }

        var devices = await deviceQueryService.EnumerateAsync(queryType, cancellationToken);
        return Results.Ok(devices);
    }

    private static bool TryParseType(string? type, out DeviceQueryType queryType)
    {
        switch (type?.ToLowerInvariant())
        {
            case "webcam":
                queryType = DeviceQueryType.Webcam;
                return true;
            case "ndi":
                queryType = DeviceQueryType.Ndi;
                return true;
            default:
                queryType = default;
                return false;
        }
    }
}
