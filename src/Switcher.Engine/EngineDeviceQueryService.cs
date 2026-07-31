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
    private readonly IVideoEngine _engine;

    public EngineDeviceQueryService(IVideoEngine engine) => _engine = engine;

    public Task<IReadOnlyList<DeviceInfo>> EnumerateAsync(DeviceQueryType type, CancellationToken ct = default)
    {
        // Webcam/NDI enumeration is delegated to libobs source-property lists via the native engine.
        return Task.FromResult(_engine.QueryDevices(type));
    }

    public Task<SrtSetupInfo> GetSrtSetupAsync(CancellationToken ct = default)
    {
        var port = ProtocolConstants.SrtListenPort;
        var hosts = LocalIPv4Candidates();
        var recommended = hosts.Count > 0 ? SrtUrl.ForSender(hosts[0], port) : $"srt://<this-pc-ip>:{port}";
        var instructions =
            "Set the sender (ATEM Mini / OBS) to SRT Caller with the URL above (latency 20-50ms). " +
            $"This PC receives as SRT Listener, which binds srt://0.0.0.0:{port}?mode=listener - that bind " +
            "URL, not the one above, is what the SRT source itself opens. Allow inbound UDP " +
            $"{port} in Windows Firewall or the sender cannot connect.";
        return Task.FromResult(new SrtSetupInfo(port, hosts, recommended, 40, instructions));
    }

    /// <summary>One of this PC's IPv4 addresses, with the two facts that say how likely a switcher on the
    /// studio LAN is to be able to reach it.</summary>
    internal readonly record struct NicCandidate(string Address, NetworkInterfaceType Type, bool HasGateway);

    private static IReadOnlyList<string> LocalIPv4Candidates()
    {
        var found = new List<NicCandidate>();
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up ||
                nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
            {
                continue;
            }

            var properties = nic.GetIPProperties();
            var hasGateway = properties.GatewayAddresses.Any(g =>
                g.Address is { AddressFamily: AddressFamily.InterNetwork } gateway &&
                !gateway.Equals(IPAddress.Any));

            foreach (var addr in properties.UnicastAddresses)
            {
                if (addr.Address.AddressFamily == AddressFamily.InterNetwork &&
                    !IPAddress.IsLoopback(addr.Address))
                {
                    found.Add(new NicCandidate(addr.Address.ToString(), nic.NetworkInterfaceType, hasGateway));
                }
            }
        }

        return Rank(found);
    }

    /// <summary>
    /// Orders the candidates so the first one is the address worth recommending. A developer PC answers
    /// this question with half a dozen addresses - Hyper-V/WSL/VirtualBox host adapters, a VPN tunnel, an
    /// unplugged NIC still holding an APIPA lease - and <c>GetAllNetworkInterfaces</c> hands them over in
    /// an order that has nothing to do with which one the switcher can reach. Recommending the wrong one
    /// makes the ATEM dial an address that answers on this PC but not from the studio network, and the
    /// stream never arrives.
    /// <para>
    /// Nothing is dropped, only sorted: the operator picks another entry when the guess is wrong, which is
    /// why every address stays in the list.
    /// </para>
    /// </summary>
    internal static IReadOnlyList<string> Rank(IEnumerable<NicCandidate> candidates) =>
        candidates
            // A 169.254.x address means DHCP never answered - it can be the recommendation only if
            // nothing else exists at all.
            .OrderBy(c => c.Address.StartsWith("169.254.", StringComparison.Ordinal) ? 1 : 0)
            // A default gateway is the strongest signal of "the network other kit is on"; the virtual
            // adapters that clutter this list almost never have one.
            .ThenBy(c => c.HasGateway ? 0 : 1)
            .ThenBy(c => c.Type is NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet
                or NetworkInterfaceType.FastEthernetT or NetworkInterfaceType.FastEthernetFx
                or NetworkInterfaceType.Wireless80211 ? 0 : 1)
            .Select(c => c.Address)
            .Distinct(StringComparer.Ordinal)
            .ToList();
}
