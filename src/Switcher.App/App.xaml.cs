using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Switcher.App.Composition;
using Switcher.App.Configuration;
using Switcher.App.Services;

namespace Switcher.App;

/// <summary>
/// Composition root and lifecycle owner (docs/tasks/agent-A-004-app-integration.md steps 1/3): builds
/// the DI container, starts every module in order, shows the main window, and keeps the app resident
/// in the tray. All module instances are disposed via the container when the operator actually exits.
/// </summary>
public partial class App : System.Windows.Application
{
    private ServiceProvider? _serviceProvider;
    private TrayIconService? _trayIcon;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        using var bootstrapLoggerFactory = LoggerFactory.Create(builder => builder.AddDebug());
        var config = AppConfigLoader.Load(AppContext.BaseDirectory, bootstrapLoggerFactory.CreateLogger("Bootstrap"));

        var services = new ServiceCollection();
        services.AddSwitcherApp(config);
        _serviceProvider = services.BuildServiceProvider();

        var logger = _serviceProvider.GetRequiredService<ILogger<App>>();

        try
        {
            await _serviceProvider.GetRequiredService<AppHostService>().StartAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "Failed to start Switcher.App core services.");
            System.Windows.MessageBox.Show($"Failed to start core services:\n{ex.Message}", "Switcher.App", MessageBoxButton.OK, MessageBoxImage.Error);
            await ExitAsync().ConfigureAwait(true);
            return;
        }

        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        MainWindow = mainWindow;
        mainWindow.Show();

        _trayIcon = new TrayIconService(
            mainWindow,
            () => _ = ExitAsync(),
            mainWindow.MoveOperatorToDisplay,
            mainWindow.OpenMultiviewFullscreen);
        mainWindow.Closing += (_, args) =>
        {
            // Stay resident in the tray; the tray "Exit" item is the only real quit path.
            args.Cancel = true;
            mainWindow.Hide();
        };
    }

    private async Task ExitAsync()
    {
        _trayIcon?.Dispose();
        _trayIcon = null;

        if (_serviceProvider is { } serviceProvider)
        {
            try
            {
                await serviceProvider.GetRequiredService<AppHostService>().StopAsync().ConfigureAwait(true);
            }
            finally
            {
                await serviceProvider.DisposeAsync().ConfigureAwait(true);
                _serviceProvider = null;
            }
        }

        Shutdown();
    }
}
