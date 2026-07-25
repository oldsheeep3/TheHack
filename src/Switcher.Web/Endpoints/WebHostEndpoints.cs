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
        endpoints.MapPost("/api/v1/sources", SourcesEndpoint.PostAsync);
        endpoints.MapPut("/api/v1/sources/{id}", SourcesEndpoint.PutAsync);
        endpoints.MapDelete("/api/v1/sources/{id}", SourcesEndpoint.DeleteAsync);

        endpoints.MapPost("/api/v1/program", ProgramEndpoint.PostAsync);
        endpoints.MapPut("/api/v1/multiview", MultiviewEndpoint.PutAsync);
        endpoints.MapPut("/api/v1/outputs", OutputsEndpoint.PutAsync);
        endpoints.MapGet("/api/v1/audio/devices", AudioEndpoint.GetDevices);
        endpoints.MapPut("/api/v1/audio/outputs", AudioEndpoint.PutOutputsAsync);
        endpoints.MapPut("/api/v1/modules", ModulesEndpoint.PutAsync);
        endpoints.MapPut("/api/v1/atem", AtemEndpoint.PutConfigAsync);
        endpoints.MapPost("/api/v1/atem/command", AtemEndpoint.PostCommandAsync);
        endpoints.MapPut("/api/v1/pico/network", PicoNetworkEndpoint.PutAsync);

        endpoints.MapGet("/api/v1/devices/{type}", DevicesEndpoint.GetAsync);
        endpoints.MapGet("/api/v1/srt/setup", SrtSetupEndpoint.GetAsync);

        // Superseded by the HID input path (agent-A2-004-hid-io); retained only for the optional
        // wireless controller fallback (docs/specs/pc-switcher-app.md §2.6).
        endpoints.Map("/ws", ControllerWebSocketEndpoint.HandleAsync);
    }
}
