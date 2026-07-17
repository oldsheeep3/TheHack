using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace Switcher.Web.Endpoints;

/// <summary>
/// Maps all Switcher.Web endpoints onto a routing target. Shared between the production
/// <see cref="WebHost"/> (Kestrel) and tests (TestServer) so both exercise identical routes.
/// </summary>
internal static class WebHostEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/v1/config", ConfigEndpoint.PostAsync);
        endpoints.MapGet("/api/v1/sources", SourcesEndpoint.Get);
        endpoints.Map("/ws", ControllerWebSocketEndpoint.HandleAsync);
    }
}
