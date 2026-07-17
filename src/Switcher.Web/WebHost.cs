using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Switcher.Contracts;
using Switcher.Web.Endpoints;

namespace Switcher.Web;

/// <summary>
/// Embedded Kestrel host exposing the config/sources REST API and the controller-input WebSocket
/// (docs/specs/pc-switcher-app.md §2.4). Constructed and owned by the app composition root; the
/// core services (<see cref="ISwitcherConfigService"/> / <see cref="IInputSourceManager"/> /
/// <see cref="IControllerInputSink"/>) are injected so this library never references their
/// concrete implementations.
/// </summary>
public sealed class WebHost : IAsyncDisposable
{
    private readonly WebApplication _app;

    public WebHost(
        ISwitcherConfigService configService,
        IInputSourceManager sourceManager,
        IControllerInputSink inputSink,
        int port = ProtocolConstants.WebPort)
    {
        ArgumentNullException.ThrowIfNull(configService);
        ArgumentNullException.ThrowIfNull(sourceManager);
        ArgumentNullException.ThrowIfNull(inputSink);

        var builder = WebApplication.CreateBuilder();

        // Binds all interfaces: controllers (Pico 2W / phone bridge) reach this over the LAN.
        // Intentionally minimal surface beyond §2.4's REST/WS endpoints even so.
        builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

        WebHostServices.Configure(builder.Services, configService, sourceManager, inputSink);

        _app = builder.Build();
        _app.UseWebSockets();
        WebHostEndpoints.Map(_app);
    }

    public Task StartAsync(CancellationToken cancellationToken = default) => _app.StartAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken = default) => _app.StopAsync(cancellationToken);

    public async ValueTask DisposeAsync() => await _app.DisposeAsync();
}
