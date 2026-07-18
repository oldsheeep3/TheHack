using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Switcher.Contracts;

namespace Switcher.Web.Tests;

public class TallyBroadcasterSerializationTests
{
    [Fact]
    public void SerializePayload_UsesActivePgmActivePvwFieldNames()
    {
        var state = new TallyState([1, 5], [2]);

        var payload = TallyBroadcaster.SerializePayload(state);
        var json = Encoding.UTF8.GetString(payload);

        Assert.Equal("""{"active_pgm":[1,5],"active_pvw":[2]}""", json);
    }

    [Fact]
    public void SerializePayload_HandlesEmptyChannelLists()
    {
        var state = new TallyState([], []);

        var payload = TallyBroadcaster.SerializePayload(state);
        var json = Encoding.UTF8.GetString(payload);

        Assert.Equal("""{"active_pgm":[],"active_pvw":[]}""", json);
    }

    [Fact]
    public void SerializePayload_V2_UsesActivePgm12ActivePvw12FieldNames()
    {
        var state = new TallyStateV2([1, 3], [2], [4], []);

        var payload = TallyBroadcaster.SerializePayload(state);
        var json = Encoding.UTF8.GetString(payload);

        Assert.Equal(
            """{"active_pgm1":[1,3],"active_pgm2":[2],"active_pvw1":[4],"active_pvw2":[]}""",
            json);
    }

    [Fact]
    public void SerializePayload_V2_HandlesEmptyChannelLists()
    {
        var state = new TallyStateV2([], [], [], []);

        var payload = TallyBroadcaster.SerializePayload(state);
        var json = Encoding.UTF8.GetString(payload);

        Assert.Equal(
            """{"active_pgm1":[],"active_pgm2":[],"active_pvw1":[],"active_pvw2":[]}""",
            json);
    }

    [Fact]
    public async Task Publish_V2_BroadcastsUdpPayloadThatRoundTripsToTheSameState()
    {
        var state = new TallyStateV2([1, 3], [2], [4], []);

        using var receiver = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var receiverPort = ((IPEndPoint)receiver.Client.LocalEndPoint!).Port;

        await using var broadcaster = new TallyBroadcaster(
            NullLogger<TallyBroadcaster>.Instance, IPAddress.Loopback.ToString(), receiverPort);

        broadcaster.Publish(state);

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var result = await receiver.ReceiveAsync(timeoutCts.Token);

        var received = JsonSerializer.Deserialize<TallyStateV2>(result.Buffer, ProtocolJsonOptions.Default);
        Assert.NotNull(received);
        Assert.Equal(state.ActivePgm1, received.ActivePgm1);
        Assert.Equal(state.ActivePgm2, received.ActivePgm2);
        Assert.Equal(state.ActivePvw1, received.ActivePvw1);
        Assert.Equal(state.ActivePvw2, received.ActivePvw2);
    }
}
