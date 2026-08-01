using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.TestHost;
using Switcher.Contracts;

namespace Switcher.Web.Tests;

/// <summary>
/// The ATEM endpoints added for the picker: reading back the current selection, sweeping for switchers,
/// and pointing a connected one's streaming output at this PC.
/// </summary>
public class AtemEndpointTests
{
    [Fact]
    public async Task GetAtem_ReturnsTheCurrentSelection()
    {
        var configService = new FakeSwitcherConfigService
        {
            CurrentAtemConfig = new AtemConfig(Enabled: true, Ip: "192.168.1.240", Mappings: [], Name: "ATEM Mini Pro"),
        };
        using var host = await CreateHostAsync(configService);
        using var client = host.GetTestClient();

        var config = await client.GetFromJsonAsync<AtemConfig>("/api/v1/atem", ProtocolJsonOptions.Default);

        Assert.Equal("192.168.1.240", config!.Ip);
        Assert.Equal("ATEM Mini Pro", config.Name);
        Assert.True(config.Enabled);
    }

    [Fact]
    public async Task GetAtemDiscover_ReturnsWhateverTheSweepFound()
    {
        var configService = new FakeSwitcherConfigService();
        configService.DiscoverableAtems.Add(new AtemDeviceInfo("192.168.1.240", "ATEM Mini Pro"));
        using var host = await CreateHostAsync(configService);
        using var client = host.GetTestClient();

        var found = await client.GetFromJsonAsync<List<AtemDeviceInfo>>("/api/v1/atem/discover", ProtocolJsonOptions.Default);

        var device = Assert.Single(found!);
        Assert.Equal("192.168.1.240", device.Ip);
        Assert.Equal("ATEM Mini Pro", device.Name);
    }

    [Fact]
    public async Task PostAtemStreaming_WithAConnectedSwitcher_ReturnsNoContentAndPassesTheUrlOn()
    {
        var configService = new FakeSwitcherConfigService();
        using var host = await CreateHostAsync(configService);
        using var client = host.GetTestClient();

        var response = await client.PostAsJsonAsync(
            "/api/v1/atem/streaming",
            new AtemStreamingRequest("srt://192.168.1.50:9000"),
            ProtocolJsonOptions.Default);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal("srt://192.168.1.50:9000", Assert.Single(configService.AtemStreamingRequests).Url);
    }

    [Fact]
    public async Task PostAtemStreaming_WithNothingConnected_ReportsAConflictRatherThanBlamingTheCaller()
    {
        var configService = new FakeSwitcherConfigService { AtemStreamingSucceeds = false };
        using var host = await CreateHostAsync(configService);
        using var client = host.GetTestClient();

        var response = await client.PostAsJsonAsync(
            "/api/v1/atem/streaming",
            new AtemStreamingRequest("srt://192.168.1.50:9000"),
            ProtocolJsonOptions.Default);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task PostAtemStreaming_WithoutAUrl_IsRejectedBeforeReachingTheSwitcher()
    {
        var configService = new FakeSwitcherConfigService();
        using var host = await CreateHostAsync(configService);
        using var client = host.GetTestClient();

        var response = await client.PostAsJsonAsync(
            "/api/v1/atem/streaming",
            new AtemStreamingRequest("   "),
            ProtocolJsonOptions.Default);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(configService.AtemStreamingRequests);
    }

    private static Task<Microsoft.Extensions.Hosting.IHost> CreateHostAsync(FakeSwitcherConfigService configService) =>
        TestWebHostFactory.CreateAsync(configService, new FakeInputSourceManager([]), new RecordingControllerInputSink());
}
