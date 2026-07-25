using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Switcher.App.Configuration;
using Switcher.App.Orchestration;
using Switcher.App.Services;
using Switcher.Atem;
using Switcher.Contracts;
using Switcher.Engine;
using Switcher.Hid;
using Switcher.Web;

namespace Switcher.App.Composition;

/// <summary>
/// Composition root: wires the concrete implementation of every module's public interface into the
/// App's DI container. Since the libobs migration the whole video pipeline (sources, dual-M/E
/// compositing, VCAM/NDI/HDMI outputs, multiview) is the single <see cref="IVideoEngine"/> abstraction
/// (<see cref="LibObsVideoEngine"/>), replacing the former GStreamer/DirectX Media + VirtualCam modules.
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSwitcherApp(this IServiceCollection services, AppConfig config)
    {
        services.AddSingleton(config);
        services.AddSingleton(sp => new RuntimeConfigStore(AppContext.BaseDirectory, sp.GetRequiredService<ILogger<RuntimeConfigStore>>()));
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Information).AddDebug());

        // The single video engine (libobs). One shared instance drives both the App UI and the embedded
        // Web host so a source added over the API is the same source the operator window renders. The
        // native switcher-engine.dll is required at runtime (Windows + OBS); see native/switcher-engine.
        services.AddSingleton<LibObsVideoEngine>();
        services.AddSingleton<IVideoEngine>(sp => sp.GetRequiredService<LibObsVideoEngine>());

        services.AddSingleton<ITallyBroadcaster, TallyBroadcaster>();

        // Device enumeration / SRT setup (docs/specs/multiview-output-revision.md §2.6/§2.7): backed by
        // the engine (libobs source-property enumeration + local-NIC SRT host discovery).
        services.AddSingleton<IDeviceQueryService, EngineDeviceQueryService>();

        // AtemController: AppOrchestrator rebuilds and hot-swaps the real mapping in its constructor,
        // so the mapping this is seeded with is never actually used.
        services.AddSingleton(ButtonCommandMapping.Empty);
        services.AddSingleton<AtemController>();

        // Switcher.Hid: HidBacklightService is a dependency of AppOrchestrator (backlight send-out);
        // HidInputService's edges are subscribed to the orchestrator once both sides exist.
        services.AddSingleton<HidBacklightService>();
        services.AddSingleton(sp =>
        {
            var hidInput = new HidInputService();
            var orchestrator = sp.GetRequiredService<AppOrchestrator>();
            hidInput.SwitchEdge += orchestrator.HandleSwitchEdge;
            hidInput.VrChanged += orchestrator.HandleVrChanged;
            // The controller's module_present bitmap is the source of truth for which modules exist:
            // rows appear when a module is attached and disappear when it is not.
            hidInput.ModulePresenceChanged += orchestrator.HandleModulePresence;
            return hidInput;
        });

        services.AddSingleton<AppOrchestrator>();
        services.AddSingleton<ISwitcherConfigService>(sp => sp.GetRequiredService<AppOrchestrator>());
        services.AddSingleton<IControllerInputSink>(sp => sp.GetRequiredService<AppOrchestrator>());

        services.AddSingleton(sp => new WebHost(
            sp.GetRequiredService<ISwitcherConfigService>(),
            sp.GetRequiredService<IVideoEngine>(),
            sp.GetRequiredService<IControllerInputSink>(),
            config.WebPort,
            sp.GetRequiredService<IDeviceQueryService>()));

        services.AddSingleton<FramePumpService>();
        services.AddSingleton<AppHostService>();

        services.AddSingleton<MainWindow>();

        return services;
    }
}
