using System.Text;
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
}
