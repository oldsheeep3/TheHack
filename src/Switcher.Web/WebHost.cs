using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Switcher.Contracts;
using Switcher.Web.Endpoints;

namespace Switcher.Web;

/// <summary>
/// Embedded Kestrel host exposing the config/sources REST API and the controller-input WebSocket
/// (docs/specs/pc-switcher-app.md §2.4). Constructed and owned by the app composition root; the
/// core services (<see cref="ISwitcherConfigService"/> / <see cref="IVideoEngine"/> /
/// <see cref="IControllerInputSink"/> / <see cref="IDeviceQueryService"/>) are injected so this
/// library never references their concrete implementations.
/// </summary>
public sealed class WebHost : IAsyncDisposable
{
    private readonly WebApplication _app;

    /// <param name="deviceQueryService">
    /// Backs <c>GET /api/v1/devices/{type}</c> and <c>GET /api/v1/srt/setup</c>. Optional so existing
    /// composition roots keep compiling; the App host is expected to inject the engine-backed concrete
    /// (docs/specs/multiview-output-revision.md §4.1). When omitted, a null-object that reports no
    /// devices is used so the endpoints stay responsive rather than throwing.
    /// </param>
    public WebHost(
        ISwitcherConfigService configService,
        IVideoEngine videoEngine,
        IControllerInputSink inputSink,
        int port = ProtocolConstants.WebPort,
        IDeviceQueryService? deviceQueryService = null)
    {
        ArgumentNullException.ThrowIfNull(configService);
        ArgumentNullException.ThrowIfNull(videoEngine);
        ArgumentNullException.ThrowIfNull(inputSink);

        var builder = WebApplication.CreateBuilder();

        // Binds all interfaces: controllers (Pico 2W / phone bridge) reach this over the LAN.
        // Intentionally minimal surface beyond §2.4's REST/WS endpoints even so.
        builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

        WebHostServices.Configure(
            builder.Services,
            configService,
            videoEngine,
            inputSink,
            deviceQueryService ?? UnavailableDeviceQueryService.Instance);

        _app = builder.Build();
        _app.UseWebSockets();
        WebHostEndpoints.Map(_app);
    }

    public Task StartAsync(CancellationToken cancellationToken = default) => _app.StartAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken = default) => _app.StopAsync(cancellationToken);

    public async ValueTask DisposeAsync() => await _app.DisposeAsync();

    /// <summary>
    /// Null-object <see cref="IDeviceQueryService"/> used when the App host has not yet injected the
    /// Media-backed implementation: enumeration returns no devices and SRT setup reports "unavailable".
    /// </summary>
    private sealed class UnavailableDeviceQueryService : IDeviceQueryService
    {
        public static readonly UnavailableDeviceQueryService Instance = new();

        public Task<IReadOnlyList<DeviceInfo>> EnumerateAsync(DeviceQueryType type, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<DeviceInfo>>(Array.Empty<DeviceInfo>());

        public Task<SrtSetupInfo> GetSrtSetupAsync(CancellationToken ct = default) =>
            Task.FromResult(new SrtSetupInfo(0, Array.Empty<string>(), string.Empty, 0, "SRT setup is unavailable."));
    }
}
