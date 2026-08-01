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
        // Literal segment, so it is matched ahead of the "{id}" routes below rather than read as an id.
        endpoints.MapGet("/api/v1/sources/definitions", SourcesEndpoint.GetDefinitionsAsync);
        endpoints.MapPost("/api/v1/sources", SourcesEndpoint.PostAsync);
        endpoints.MapPut("/api/v1/sources/{id}", SourcesEndpoint.PutAsync);
        endpoints.MapDelete("/api/v1/sources/{id}", SourcesEndpoint.DeleteAsync);

        endpoints.MapPost("/api/v1/program", ProgramEndpoint.PostAsync);
        endpoints.MapGet("/api/v1/multiview", MultiviewEndpoint.GetAsync);
        endpoints.MapPut("/api/v1/multiview", MultiviewEndpoint.PutAsync);
        endpoints.MapGet("/api/v1/outputs", OutputsEndpoint.GetAsync);
        endpoints.MapPut("/api/v1/outputs", OutputsEndpoint.PutAsync);
        endpoints.MapGet("/api/v1/audio/devices", AudioEndpoint.GetDevices);
        endpoints.MapGet("/api/v1/audio/outputs", AudioEndpoint.GetOutputsAsync);
        endpoints.MapPut("/api/v1/audio/outputs", AudioEndpoint.PutOutputsAsync);
        endpoints.MapGet("/api/v1/modules", ModulesEndpoint.GetAsync);
        endpoints.MapPut("/api/v1/modules", ModulesEndpoint.PutAsync);
        endpoints.MapGet("/api/v1/atem", AtemEndpoint.GetConfigAsync);
        endpoints.MapPut("/api/v1/atem", AtemEndpoint.PutConfigAsync);
        endpoints.MapGet("/api/v1/atem/discover", AtemEndpoint.GetDevicesAsync);
        endpoints.MapPost("/api/v1/atem/streaming", AtemEndpoint.PostStreamingAsync);
        endpoints.MapPost("/api/v1/atem/command", AtemEndpoint.PostCommandAsync);
        endpoints.MapPut("/api/v1/pico/network", PicoNetworkEndpoint.PutAsync);

        endpoints.MapGet("/api/v1/devices/{type}", DevicesEndpoint.GetAsync);
        endpoints.MapGet("/api/v1/srt/setup", SrtSetupEndpoint.GetAsync);

        // Superseded by the HID input path (agent-A2-004-hid-io); retained only for the optional
        // wireless controller fallback (docs/specs/pc-switcher-app.md §2.6).
        endpoints.Map("/ws", ControllerWebSocketEndpoint.HandleAsync);
    }
}
