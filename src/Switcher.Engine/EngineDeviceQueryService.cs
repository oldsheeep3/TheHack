using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Switcher.Contracts;

namespace Switcher.Engine;

/// <summary>
/// <see cref="IDeviceQueryService"/> backed by the video engine (docs/specs/multiview-output-revision.md
/// §4.1). SRT setup (LAN IPv4 candidates + recommended URL) is computed purely managed-side from the
/// local NICs; webcam/NDI device enumeration is delegated to libobs source-property enumeration in the
/// native layer (TODO(L-002)) and reports an empty list until that lands, so the endpoints stay
/// responsive rather than throwing.
/// </summary>
public sealed class EngineDeviceQueryService : IDeviceQueryService
{
    public Task<IReadOnlyList<DeviceInfo>> EnumerateAsync(DeviceQueryType type, CancellationToken ct = default)
    {
        // TODO(L-002): enumerate via the native engine (dshow_input / DistroAV source properties).
        IReadOnlyList<DeviceInfo> devices = [];
        return Task.FromResult(devices);
    }

    public Task<SrtSetupInfo> GetSrtSetupAsync(CancellationToken ct = default)
    {
        var port = ProtocolConstants.SrtListenPort;
        var hosts = LocalIPv4Candidates();
        var recommended = hosts.Count > 0 ? $"srt://{hosts[0]}:{port}" : $"srt://<this-pc-ip>:{port}";
        var instructions =
            "Set the ATEM Mini's SRT streaming output to Caller with the URL above (latency 20-50ms). " +
            "This PC listens as SRT Listener on the port shown.";
        return Task.FromResult(new SrtSetupInfo(port, hosts, recommended, 40, instructions));
    }

    private static IReadOnlyList<string> LocalIPv4Candidates()
    {
        var result = new List<string>();
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up ||
                nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
            {
                continue;
            }

            foreach (var addr in nic.GetIPProperties().UnicastAddresses)
            {
                if (addr.Address.AddressFamily == AddressFamily.InterNetwork &&
                    !IPAddress.IsLoopback(addr.Address))
                {
                    result.Add(addr.Address.ToString());
                }
            }
        }

        return result;
    }
}
