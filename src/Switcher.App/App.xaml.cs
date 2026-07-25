using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Switcher.App.Composition;
using Switcher.App.Configuration;
using Switcher.App.Logging;
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

        using var bootstrapLoggerFactory = LoggerFactory.Create(builder => builder
            .AddDebug()
            .AddProvider(new FileLoggerProvider(AppPaths.LogDirectory)));
        var bootstrapLogger = bootstrapLoggerFactory.CreateLogger("Bootstrap");
        var config = AppConfigLoader.Load(AppContext.BaseDirectory, bootstrapLogger);

        var services = new ServiceCollection();
        services.AddSwitcherApp(config);
        _serviceProvider = services.BuildServiceProvider();

        var logger = _serviceProvider.GetRequiredService<ILogger<App>>();
        InstallGlobalExceptionHandlers(logger);
        logger.LogInformation("Switcher.App starting. Logs: {LogDirectory}", AppPaths.LogDirectory);

        try
        {
            await _serviceProvider.GetRequiredService<AppHostService>().StartAsync().ConfigureAwait(true);

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
        catch (Exception ex)
        {
            // Building the operator window is part of starting up: it queries the engine and the display
            // list, and a failure there used to escape this async void and end the process with no log.
            logger.LogCritical(ex, "Failed to start Switcher.App core services.");
            System.Windows.MessageBox.Show(
                $"Failed to start core services:\n{ex.Message}\n\nDetails: {AppPaths.LogDirectory}",
                "Switcher.App", MessageBoxButton.OK, MessageBoxImage.Error);
            await ExitAsync().ConfigureAwait(true);
        }
    }

    /// <summary>
    /// Catches what the per-operation handlers do not.
    ///
    /// A switcher that vanishes mid-show is the worst possible failure, so a UI-thread exception is
    /// logged and swallowed rather than allowed to end the process — the operator can still cut away,
    /// and the log says what happened. The other two hooks cannot prevent termination; they exist so the
    /// crash is not silent.
    /// </summary>
    private void InstallGlobalExceptionHandlers(ILogger logger)
    {
        DispatcherUnhandledException += (_, args) =>
        {
            logger.LogError(args.Exception, "Unhandled exception on the UI thread.");
            args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            logger.LogCritical(args.ExceptionObject as Exception, "Unhandled exception; the process is terminating.");

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            logger.LogError(args.Exception, "Unobserved task exception.");
            args.SetObserved();
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
