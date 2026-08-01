using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Switcher.Atem.Discovery;

/// <summary>
/// Works out which addresses a "find my ATEM" sweep should knock on: every host address of every
/// IPv4 subnet this PC has an operational NIC in. ATEMs answer a unicast hello but do not advertise
/// themselves on any discovery protocol we can rely on, so an address sweep is what is left.
/// </summary>
public static class AtemScanTargets
{
    /// <summary>
    /// Largest subnet worth sweeping, in host addresses. A /24 (254 hosts) is the normal studio LAN and
    /// sweeps in about a second; anything wider than a /22 is skipped rather than firing tens of
    /// thousands of datagrams at a network the operator may not own.
    /// </summary>
    public const int MaxHostsPerSubnet = 1024;

    /// <summary>Host addresses of every local IPv4 subnet, excluding this PC's own addresses.</summary>
    public static IReadOnlyList<IPAddress> FromLocalSubnets()
    {
        var local = new HashSet<uint>();
        var targets = new List<IPAddress>();
        var seen = new HashSet<uint>();

        foreach (var (address, mask) in LocalIPv4Networks())
        {
            local.Add(ToUInt32(address));

            var maskBits = ToUInt32(mask);
            var network = ToUInt32(address) & maskBits;
            var hostCount = ~maskBits;

            // hostCount counts the network+broadcast pair as well, hence the -1.
            if (hostCount is 0 or > MaxHostsPerSubnet + 1)
            {
                continue;
            }

            for (var host = 1u; host < hostCount; host++)
            {
                var candidate = network + host;
                if (seen.Add(candidate))
                {
                    targets.Add(FromUInt32(candidate));
                }
            }
        }

        return [.. targets.Where(t => !local.Contains(ToUInt32(t)))];
    }

    private static IEnumerable<(IPAddress Address, IPAddress Mask)> LocalIPv4Networks()
    {
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up ||
                nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
            {
                continue;
            }

            foreach (var unicast in nic.GetIPProperties().UnicastAddresses)
            {
                if (unicast.Address.AddressFamily == AddressFamily.InterNetwork &&
                    unicast.IPv4Mask is { } mask &&
                    !Equals(mask, IPAddress.Any))
                {
                    yield return (unicast.Address, mask);
                }
            }
        }
    }

    private static uint ToUInt32(IPAddress address)
    {
        Span<byte> bytes = stackalloc byte[4];
        address.TryWriteBytes(bytes, out _);
        return ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];
    }

    private static IPAddress FromUInt32(uint value) =>
        new([(byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value]);
}
