using System.Net.NetworkInformation;
using Switcher.Engine;

using NicCandidate = Switcher.Engine.EngineDeviceQueryService.NicCandidate;

namespace Switcher.Engine.Tests;

/// <summary>
/// Which of this PC's addresses gets recommended as the SRT target the ATEM should dial
/// (docs/specs/multiview-output-revision.md §2.7). A studio PC answers on several - a Hyper-V or
/// VirtualBox host adapter, a VPN tunnel, a NIC still holding an APIPA lease - and the OS hands them
/// over in an arbitrary order, so recommending the first one it happens to list points the switcher at
/// an address that is unreachable from the studio network.
/// </summary>
public class SrtHostCandidateTests
{
    [Fact]
    public void Rank_PutsTheAddressWithADefaultGatewayFirst()
    {
        var ranked = EngineDeviceQueryService.Rank(
        [
            new NicCandidate("172.20.208.1", NetworkInterfaceType.Ethernet, HasGateway: false),   // vEthernet (WSL)
            new NicCandidate("192.168.10.20", NetworkInterfaceType.Ethernet, HasGateway: true),   // the studio LAN
        ]);

        Assert.Equal("192.168.10.20", ranked[0]);
    }

    [Fact]
    public void Rank_KeepsEveryAddressSoAnotherOneCanBePicked()
    {
        var ranked = EngineDeviceQueryService.Rank(
        [
            new NicCandidate("192.168.56.1", NetworkInterfaceType.Ethernet, HasGateway: false),
            new NicCandidate("192.168.10.20", NetworkInterfaceType.Ethernet, HasGateway: true),
            new NicCandidate("10.8.0.6", NetworkInterfaceType.Tunnel, HasGateway: false),
        ]);

        // The recommendation is a guess; the operator overrides it from this list, so nothing may be
        // filtered out of it.
        Assert.Equal(3, ranked.Count);
        Assert.Contains("192.168.56.1", ranked);
        Assert.Contains("10.8.0.6", ranked);
    }

    [Fact]
    public void Rank_SinksAnApipaAddressToTheBottomWithoutDroppingIt()
    {
        var ranked = EngineDeviceQueryService.Rank(
        [
            new NicCandidate("169.254.33.7", NetworkInterfaceType.Ethernet, HasGateway: false),
            new NicCandidate("192.168.56.1", NetworkInterfaceType.Ethernet, HasGateway: false),
        ]);

        // 169.254.x means DHCP never answered - it can never be the recommendation while anything else
        // exists, but a hand-configured link-local pairing is still someone's setup.
        Assert.Equal(["192.168.56.1", "169.254.33.7"], ranked);
    }

    [Fact]
    public void Rank_WithNoGatewayAnywhere_PrefersAPhysicalNicOverATunnel()
    {
        var ranked = EngineDeviceQueryService.Rank(
        [
            new NicCandidate("10.8.0.6", NetworkInterfaceType.Tunnel, HasGateway: false),
            new NicCandidate("192.168.10.20", NetworkInterfaceType.Wireless80211, HasGateway: false),
        ]);

        Assert.Equal("192.168.10.20", ranked[0]);
    }

    [Fact]
    public void Rank_LeavesEquallyGoodAddressesInTheOrderTheOsReportedThem()
    {
        var ranked = EngineDeviceQueryService.Rank(
        [
            new NicCandidate("192.168.10.20", NetworkInterfaceType.Ethernet, HasGateway: true),
            new NicCandidate("192.168.10.21", NetworkInterfaceType.Ethernet, HasGateway: true),
            new NicCandidate("192.168.10.20", NetworkInterfaceType.Ethernet, HasGateway: true),
        ]);

        // One NIC can hold several addresses; the same address must not appear twice in the picker.
        Assert.Equal(["192.168.10.20", "192.168.10.21"], ranked);
    }

    [Fact]
    public void Rank_WithNothingToRank_ReturnsNothing() =>
        Assert.Empty(EngineDeviceQueryService.Rank([]));
}
