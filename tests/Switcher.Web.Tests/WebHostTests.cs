namespace Switcher.Web.Tests;

public class WebHostTests
{
    [Fact]
    public async Task StartAsync_ThenStopAsync_DoesNotThrow()
    {
        // port: 0 lets Kestrel bind an ephemeral port so this test can run concurrently with others.
        await using var host = new WebHost(
            new FakeSwitcherConfigService(),
            new FakeInputSourceManager([]),
            new RecordingControllerInputSink(),
            port: 0);

        await host.StartAsync();
        await host.StopAsync();
    }
}
