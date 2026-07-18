using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Switcher.App.Configuration;
using Switcher.App.Orchestration;
using Switcher.App.Services;
using Switcher.Atem;
using Switcher.Contracts;
using Switcher.Hid;
using Switcher.Media;
using Switcher.VirtualCam;
using Switcher.VirtualCam.Display;
using Switcher.Web;

namespace Switcher.App.Composition;

/// <summary>
/// Composition root: wires the concrete implementation of every module's public interface into the
/// App's DI container (docs/tasks/agent-A2-006-app-integration-v2.md step 1). App never references a
/// module's internals beyond what its public constructor/contract requires, except where the v2
/// dual-ME/HID/dual-output surface is concrete-only (see the note on <see cref="AppOrchestrator"/>).
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSwitcherApp(this IServiceCollection services, AppConfig config)
    {
        services.AddSingleton(config);
        services.AddSingleton(sp => new RuntimeConfigStore(AppContext.BaseDirectory, sp.GetRequiredService<ILogger<RuntimeConfigStore>>()));
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Information).AddDebug());

        // InputSourceManager/CompositorEngine: CompositorEngine's public constructor requires the
        // concrete InputSourceManager (it reads frames through an internal-to-Media interface), so
        // both the concrete singleton and the IInputSourceManager mapping (consumed by Switcher.Web)
        // must resolve to the same instance.
        services.AddSingleton<InputSourceManager>();
        services.AddSingleton<IInputSourceManager>(sp => sp.GetRequiredService<InputSourceManager>());
        services.AddSingleton<CompositorEngine>();

        services.AddSingleton<ITallyBroadcaster, TallyBroadcaster>();

        // AtemController: AppOrchestrator rebuilds and hot-swaps the real mapping (from AppConfig's
        // static bindings + the persisted RuntimeConfig's Web-driven AtemConfig) in its constructor,
        // so the mapping this is seeded with is never actually used.
        services.AddSingleton(ButtonCommandMapping.Empty);
        services.AddSingleton<AtemController>();

        // Dual virtual camera / HDMI fullscreen output + the router that fans PGM1/PGM2 frames out to
        // whichever sinks PUT /api/v1/outputs currently assigns them to.
        services.AddSingleton<IDualVirtualCameraOutput, DualVirtualCameraOutput>();
        services.AddSingleton<IHdmiFullscreenOutput, HdmiFullscreenOutput>();
        services.AddSingleton(sp => new OutputRouter(
            sp.GetRequiredService<IDualVirtualCameraOutput>(),
            sp.GetRequiredService<IHdmiFullscreenOutput>()));

        // Switcher.Hid: HidBacklightService is a dependency of AppOrchestrator (backlight send-out);
        // HidInputService has no dependents, only dependents-of-it (AppOrchestrator's event handlers),
        // so its registration wires the subscription directly once both sides exist.
        services.AddSingleton<HidBacklightService>();
        services.AddSingleton(sp =>
        {
            var hidInput = new HidInputService();
            var orchestrator = sp.GetRequiredService<AppOrchestrator>();
            hidInput.SwitchEdge += orchestrator.HandleSwitchEdge;
            hidInput.VrChanged += orchestrator.HandleVrChanged;
            return hidInput;
        });

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
