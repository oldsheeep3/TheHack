using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Switcher.Contracts;
using Switcher.Web.Endpoints;

namespace Switcher.Web.Tests;

/// <summary>
/// Builds an in-memory TestServer that maps the exact same DI/routes as the production
/// <see cref="WebHost"/> (via the shared internal <see cref="WebHostServices"/>/<see cref="WebHostEndpoints"/>),
/// without binding a real socket.
/// </summary>
internal static class TestWebHostFactory
{
    public static async Task<IHost> CreateAsync(
        ISwitcherConfigService configService,
        IInputSourceManager sourceManager,
        IControllerInputSink inputSink)
    {
        var hostBuilder = new HostBuilder()
            .ConfigureWebHost(webHostBuilder =>
            {
                webHostBuilder
                    .UseTestServer()
                    .ConfigureServices(services =>
                    {
                        services.AddRouting();
                        WebHostServices.Configure(services, configService, sourceManager, inputSink);
                    })
                    .Configure(app =>
                    {
                        app.UseRouting();
                        app.UseWebSockets();
                        app.UseEndpoints(WebHostEndpoints.Map);
                    });
            });

        return await hostBuilder.StartAsync();
    }
}
