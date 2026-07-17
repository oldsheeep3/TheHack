using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Switcher.App.Configuration;
using Switcher.App.Orchestration;
using Switcher.App.Services;
using Switcher.Atem;
using Switcher.Contracts;
using Switcher.Media;
using Switcher.VirtualCam;
using Switcher.Web;

namespace Switcher.App.Composition;

/// <summary>
/// Composition root: wires the concrete implementation of every module's public interface into the
/// App's DI container (docs/tasks/agent-A-004-app-integration.md step 1). App never references a
/// module's internals beyond what its public constructor/contract requires.
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSwitcherApp(this IServiceCollection services, AppConfig config)
    {
        services.AddSingleton(config);
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Information).AddDebug());

        // InputSourceManager/CompositorEngine: CompositorEngine's public constructor requires the
        // concrete InputSourceManager (it reads frames through an internal-to-Media interface), so
        // both the concrete singleton and the IInputSourceManager mapping must resolve to the same
        // instance.
        services.AddSingleton<InputSourceManager>();
        services.AddSingleton<IInputSourceManager>(sp => sp.GetRequiredService<InputSourceManager>());
        services.AddSingleton<ICompositorEngine>(sp => new CompositorEngine(sp.GetRequiredService<InputSourceManager>()));

        services.AddSingleton<ITallyBroadcaster, TallyBroadcaster>();

        // AtemController: same "concrete + interface -> same instance" shape, since the UI reads the
        // public ConnectionStateChanged/State surface that IAtemController does not expose.
        services.AddSingleton(sp => new ButtonCommandMapping(config.AtemButtonMappings.ToDictionary(
            m => (m.ControllerId, m.ButtonId),
            m => new AtemCommandMapping(m.Action, m.MixEffect, m.Source))));
        services.AddSingleton<AtemController>();
        services.AddSingleton<IAtemController>(sp => sp.GetRequiredService<AtemController>());

        services.AddSingleton<IVirtualCameraOutput, VirtualCameraOutput>();

        services.AddSingleton<AppOrchestrator>();
        services.AddSingleton<ISwitcherConfigService>(sp => sp.GetRequiredService<AppOrchestrator>());
        services.AddSingleton<IControllerInputSink>(sp => sp.GetRequiredService<AppOrchestrator>());

        services.AddSingleton(sp => new WebHost(
            sp.GetRequiredService<ISwitcherConfigService>(),
            sp.GetRequiredService<IInputSourceManager>(),
            sp.GetRequiredService<IControllerInputSink>(),
            config.WebPort));

        services.AddSingleton<FramePumpService>();
        services.AddSingleton<AppHostService>();

        services.AddSingleton<MainWindow>();

        return services;
    }
}
