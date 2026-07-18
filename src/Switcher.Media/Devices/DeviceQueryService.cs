using Microsoft.Extensions.Logging;
using Switcher.Contracts;

namespace Switcher.Media.Devices;

/// <summary>
/// Default <see cref="IDeviceQueryService"/> (docs/specs/multiview-output-revision.md §2.6, §2.7).
/// Enumerates webcam/NDI devices and computes SRT listener guidance for this PC. All OS-specific work
/// is delegated to injectable providers (<see cref="IWebcamDeviceProvider"/>,
/// <see cref="INdiSourceProvider"/>, <see cref="ILocalAddressProvider"/>), and every provider call is
/// failure-isolated: an unavailable NDI SDK, a capture backend error, or a NIC probe failure yields an
/// empty result instead of propagating, so device discovery never blocks source management.
/// </summary>
public sealed class DeviceQueryService : IDeviceQueryService
{
    /// <summary>SRT recommended latency (docs/specs/multiview-output-revision.md §2.7: 20-50ms),
    /// aligned with the pipeline's fixed midpoint in <c>PipelineDescriptorFactory</c>.</summary>
    public const int RecommendedLatencyMs = 40;

    private const string SrtInstructions =
        "This PC listens for an incoming SRT stream (Listener mode) on the port below. In ATEM Mini's " +
        "streaming settings, choose Caller and enter the recommended URL (srt://<this-pc-ip>:<port>). " +
        "Use one of the host candidates that shares the ATEM's network. A latency of 20-50 ms is " +
        "recommended (40 ms default). Alternatively, set this PC to Caller and the sender to Listener.";

    private readonly IWebcamDeviceProvider _webcamProvider;
    private readonly INdiSourceProvider _ndiProvider;
    private readonly ILocalAddressProvider _addressProvider;
    private readonly ILogger<DeviceQueryService> _logger;

    public DeviceQueryService(ILoggerFactory loggerFactory)
        : this(
            new GStreamerWebcamDeviceProvider(loggerFactory.CreateLogger<GStreamerWebcamDeviceProvider>()),
            new GStreamerNdiSourceProvider(loggerFactory.CreateLogger<GStreamerNdiSourceProvider>()),
            new LocalAddressProvider(),
            loggerFactory)
    {
    }

    internal DeviceQueryService(
        IWebcamDeviceProvider webcamProvider,
        INdiSourceProvider ndiProvider,
        ILocalAddressProvider addressProvider,
        ILoggerFactory loggerFactory)
    {
        _webcamProvider = webcamProvider;
        _ndiProvider = ndiProvider;
        _addressProvider = addressProvider;
        _logger = loggerFactory.CreateLogger<DeviceQueryService>();
    }

    public Task<IReadOnlyList<DeviceInfo>> EnumerateAsync(DeviceQueryType type, CancellationToken ct = default)
    {
        var devices = type switch
        {
            DeviceQueryType.Webcam => SafeEnumerate(_webcamProvider.Enumerate, "webcam"),
            DeviceQueryType.Ndi => SafeEnumerate(_ndiProvider.Enumerate, "ndi"),
            _ => Array.Empty<DeviceInfo>(),
        };

        return Task.FromResult(devices);
    }

    public Task<SrtSetupInfo> GetSrtSetupAsync(CancellationToken ct = default)
    {
        IReadOnlyList<string> hosts;
        try
        {
            hosts = _addressProvider.GetLanIPv4Addresses() ?? Array.Empty<string>();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LAN address enumeration failed; returning no host candidates.");
            hosts = Array.Empty<string>();
        }

        var port = ProtocolConstants.SrtListenPort;
        var primaryHost = hosts.Count > 0 ? hosts[0] : "<this-pc-ip>";
        var recommendedUrl = $"srt://{primaryHost}:{port}";

        return Task.FromResult(new SrtSetupInfo(
            ListenerPort: port,
            HostCandidates: hosts,
            RecommendedUrl: recommendedUrl,
            RecommendedLatencyMs: RecommendedLatencyMs,
            InstructionsText: SrtInstructions));
    }

    private IReadOnlyList<DeviceInfo> SafeEnumerate(Func<IReadOnlyList<DeviceInfo>> enumerate, string label)
    {
        try
        {
            return enumerate() ?? Array.Empty<DeviceInfo>();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "{Kind} device enumeration failed; returning an empty list.", label);
            return Array.Empty<DeviceInfo>();
        }
    }
}
