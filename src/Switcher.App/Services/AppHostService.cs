using Microsoft.Extensions.Logging;
using Switcher.App.Configuration;
using Switcher.Atem;
using Switcher.Web;

namespace Switcher.App.Services;

/// <summary>
/// Owns the ordered startup/shutdown sequence for the resident app (docs/tasks/agent-A-004-app-integration.md
/// step 3): connect the ATEM client, start the embedded web host (config/sources API + controller
/// WebSocket), then start the frame pump (virtual camera + UI feed). Shutdown reverses the order.
/// Final disposal of the underlying module instances (InputSourceManager, CompositorEngine,
/// AtemController, VirtualCameraOutput, TallyBroadcaster) happens when the DI container itself is
/// disposed in <c>App.xaml.cs</c> - this class only owns the start/stop *sequence*.
/// </summary>
public sealed class AppHostService
{
    private readonly WebHost _webHost;
    private readonly AtemController _atemController;
    private readonly FramePumpService _framePump;
    private readonly AppConfig _config;
    private readonly ILogger<AppHostService> _logger;

    public AppHostService(WebHost webHost, AtemController atemController, FramePumpService framePump, AppConfig config, ILogger<AppHostService> logger)
    {
        _webHost = webHost;
        _atemController = atemController;
        _framePump = framePump;
        _config = config;
        _logger = logger;
    }

    public async Task StartAsync()
    {
        _logger.LogInformation("Starting Switcher.App: connecting ATEM client to {AtemIp}.", _config.AtemIp);
        _atemController.Connect(_config.AtemIp);

        _logger.LogInformation("Starting embedded web host on port {WebPort}.", _config.WebPort);
        await _webHost.StartAsync().ConfigureAwait(false);

        _logger.LogInformation("Starting frame pump.");
        _framePump.Start();
    }

    public async Task StopAsync()
    {
        _logger.LogInformation("Stopping Switcher.App.");

        await _framePump.StopAsync().ConfigureAwait(false);
        await _webHost.StopAsync().ConfigureAwait(false);
        _atemController.Dispose();
    }
}
