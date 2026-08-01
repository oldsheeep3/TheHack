using System.Net;

namespace Switcher.Web.Tests;

/// <summary>
/// The settings UI is served from the API's own port so the page and the endpoints it calls share an
/// origin. Without that, the browser blocks every <c>/api/v1/*</c> call the page makes unless the host —
/// which binds 0.0.0.0 — starts handing out CORS headers to the whole LAN.
/// </summary>
public class WebUiHostingTests : IDisposable
{
    private readonly string _uiRoot = Path.Combine(Path.GetTempPath(), $"switcher-ui-{Guid.NewGuid():N}");

    public WebUiHostingTests()
    {
        Directory.CreateDirectory(_uiRoot);
        File.WriteAllText(Path.Combine(_uiRoot, "index.html"), "<!doctype html><title>Switcher</title>");
        Directory.CreateDirectory(Path.Combine(_uiRoot, "assets"));
        File.WriteAllText(Path.Combine(_uiRoot, "assets", "index.js"), "export const ok = true");
    }

    public void Dispose()
    {
        Directory.Delete(_uiRoot, recursive: true);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task Root_ServesTheDeployedIndexHtml()
    {
        await using var host = CreateHost();
        await host.StartAsync();
        using var client = new HttpClient();

        var response = await client.GetAsync(BaseAddress(host));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Switcher", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        await host.StopAsync();
    }

    [Fact]
    public async Task Assets_AreServedFromTheSameOriginAsTheApi()
    {
        await using var host = CreateHost();
        await host.StartAsync();
        using var client = new HttpClient();

        var asset = await client.GetAsync(new Uri(BaseAddress(host), "assets/index.js"));
        var api = await client.GetAsync(new Uri(BaseAddress(host), "api/v1/sources"));

        Assert.Equal(HttpStatusCode.OK, asset.StatusCode);
        Assert.Equal(HttpStatusCode.OK, api.StatusCode);
        await host.StopAsync();
    }

    [Fact]
    public async Task WithNoUiDeployed_TheApiStillStartsAndServes()
    {
        // CI and a dev machine that never ran `npm run build` have no wwwroot; that must not be fatal.
        await using var host = new WebHost(
            new FakeSwitcherConfigService(),
            new FakeInputSourceManager([]),
            new RecordingControllerInputSink(),
            port: 0,
            webUiPath: Path.Combine(_uiRoot, "does-not-exist"));

        await host.StartAsync();
        using var client = new HttpClient();

        Assert.Null(host.WebUiPath);
        var api = await client.GetAsync(new Uri(BaseAddress(host), "api/v1/sources"));
        Assert.Equal(HttpStatusCode.OK, api.StatusCode);
        await host.StopAsync();
    }

    private WebHost CreateHost() => new(
        new FakeSwitcherConfigService(),
        new FakeInputSourceManager([]),
        new RecordingControllerInputSink(),
        port: 0,
        webUiPath: _uiRoot);

    /// <summary>The address Kestrel actually bound, since port 0 leaves the choice to the OS.</summary>
    private static Uri BaseAddress(WebHost host)
    {
        var url = Assert.Single(host.Urls);
        // Kestrel reports the wildcard bind as 0.0.0.0, which is not a connectable destination.
        return new Uri(url.Replace("0.0.0.0", "127.0.0.1", StringComparison.Ordinal) + "/");
    }
}
