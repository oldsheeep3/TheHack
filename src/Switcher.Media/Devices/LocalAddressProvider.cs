using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Switcher.Media.Devices;

/// <summary>
/// Default <see cref="ILocalAddressProvider"/>: enumerates operational NICs' unicast IPv4 addresses,
/// excluding loopback (127.0.0.0/8) and link-local/APIPA (169.254.0.0/16) so only routable LAN
/// candidates are offered for an incoming SRT stream (docs/specs/multiview-output-revision.md §2.7).
/// </summary>
internal sealed class LocalAddressProvider : ILocalAddressProvider
{
    public IReadOnlyList<string> GetLanIPv4Addresses()
    {
        var addresses = new List<string>();

        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up ||
                nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
            {
                continue;
            }

            foreach (var unicast in nic.GetIPProperties().UnicastAddresses)
            {
                var ip = unicast.Address;
                if (ip.AddressFamily != AddressFamily.InterNetwork ||
                    IPAddress.IsLoopback(ip) ||
                    IsLinkLocal(ip))
                {
                    continue;
                }

                var text = ip.ToString();
                if (!addresses.Contains(text))
                {
                    addresses.Add(text);
                }
            }
        }

        return addresses;
    }

    private static bool IsLinkLocal(IPAddress ip)
    {
        var bytes = ip.GetAddressBytes();
        return bytes[0] == 169 && bytes[1] == 254;
    }
}
