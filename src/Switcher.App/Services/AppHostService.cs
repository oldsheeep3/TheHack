using Microsoft.Extensions.Logging;
using Switcher.App.Configuration;
using Switcher.App.Orchestration;
using Switcher.Atem;
using Switcher.Contracts;
using Switcher.Hid;
using Switcher.Web;

namespace Switcher.App.Services;

/// <summary>
/// Owns the ordered startup/shutdown sequence for the resident app (docs/tasks/agent-A2-006-app-integration-v2.md
/// step 6): connect the ATEM client, start the HID input/backlight services, start the embedded web
/// host (config/sources API + the deprecated controller WebSocket fallback), then start the frame pump
/// (virtual camera/HDMI output + UI feed). Shutdown reverses the order. HID is the default control
/// path (docs/specs/pc-switcher-app.md §2.6); the WebSocket endpoint mapped by <see cref="WebHost"/>
/// (<c>/ws</c>) is retained only for the optional wireless controller fallback and needs no separate
/// start/stop step here - it activates as soon as the web host is listening. Final disposal of the
/// underlying module instances happens when the DI container itself is disposed in
/// <c>App.xaml.cs</c> - this class only owns the start/stop *sequence*.
/// </summary>
public sealed class AppHostService
{
    private readonly IVideoEngine _engine;
    private readonly AppOrchestrator _orchestrator;
    private readonly WebHost _webHost;
    private readonly AtemController _atemController;
    private readonly HidInputService _hidInputService;
    private readonly HidBacklightService _hidBacklightService;
    private readonly FramePumpService _framePump;
    private readonly AppConfig _config;
    private readonly ILogger<AppHostService> _logger;

    public AppHostService(
        IVideoEngine engine,
        AppOrchestrator orchestrator,
        WebHost webHost,
        AtemController atemController,
        HidInputService hidInputService,
        HidBacklightService hidBacklightService,
        FramePumpService framePump,
        AppConfig config,
        ILogger<AppHostService> logger)
    {
        _engine = engine;
        _orchestrator = orchestrator;
        _webHost = webHost;
        _atemController = atemController;
        _hidInputService = hidInputService;
        _hidBacklightService = hidBacklightService;
        _framePump = framePump;
        _config = config;
        _logger = logger;
    }

    public async Task StartAsync()
    {
        _logger.LogInformation("Starting Switcher.App: booting video engine.");

        // Resolve the installed OBS runtime and feed libobs its core data / plugin paths. Without the core
        // data path the native engine's obs_reset_video fails ("Native switcher-engine failed to start").
        var engineOptions = ObsRuntime.Configure(new EngineOptions(), _config.ObsInstallPath, _logger);
        await _engine.StartAsync(engineOptions).ConfigureAwait(false);

        // Replay persisted state now that the engine's native context exists (the orchestrator was
        // constructed during DI build, before the engine started, so it defers this from its ctor).
        // Sources/multiview must be restored before MainWindow is constructed - it seeds its tiles and
        // multiview cells from the engine and the orchestrator's layout.
        _orchestrator.RestorePersistedOutputs();
        _orchestrator.RestorePersistedSources();
        _orchestrator.RestorePersistedAudio();

        _logger.LogInformation("Connecting ATEM client to {AtemIp}.", _config.AtemIp);
        _atemController.Connect(_config.AtemIp);

        try
        {
            _logger.LogInformation("Starting HID input/backlight services.");
            _hidInputService.Start();
            _hidBacklightService.Start();
        }
        catch (Exception ex)
        {
            // No Pico 2W controller attached is a normal dev/CI configuration, not a fatal error - the
            // rest of the app (Web/ATEM/compositor) must still start (docs/specs/00-system-overview.md
            // §5: 1つの障害が全体を止めない).
            _logger.LogWarning(ex, "HID controller unavailable; continuing without physical module input.");
        }

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
        _hidBacklightService.Stop();
        _hidInputService.Stop();
        _atemController.Dispose();
        await _engine.StopAsync().ConfigureAwait(false);
    }
}
