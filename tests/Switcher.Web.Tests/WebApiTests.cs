using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Switcher.Contracts;

namespace Switcher.Web.Tests;

public class WebApiTests
{
    [Fact]
    public async Task PostConfig_WithValidRequest_ReturnsNoContentAndDelegatesToService()
    {
        var configService = new FakeSwitcherConfigService();
        var sourceManager = new FakeInputSourceManager([]);
        using var host = await TestWebHostFactory.CreateAsync(configService, sourceManager, new RecordingControllerInputSink());
        using var client = host.GetTestClient();

        var payload = """
            {
              "target_channel": 2,
              "source_type": "SRT",
              "source_url": "srt://192.168.1.100:9000?mode=caller",
              "pip_settings": {
                "enabled": true,
                "x_position": 1420,
                "y_position": 80,
                "width": 480,
                "height": 270,
                "opacity": 1.0,
                "z_order": 0,
                "crop": null
              }
            }
            """;

        var response = await client.PostAsync(
            "/api/v1/config",
            new StringContent(payload, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var received = Assert.Single(configService.Received);
        Assert.Equal(2, received.TargetChannel);
        Assert.Equal(SourceProtocol.Srt, received.SourceType);
        Assert.NotNull(received.PipSettings);
        Assert.Equal(1420, received.PipSettings!.X);
    }

    [Fact]
    public async Task PostConfig_WithInvalidRequest_ReturnsBadRequestAndDoesNotCallService()
    {
        var configService = new FakeSwitcherConfigService();
        var sourceManager = new FakeInputSourceManager([]);
        using var host = await TestWebHostFactory.CreateAsync(configService, sourceManager, new RecordingControllerInputSink());
        using var client = host.GetTestClient();

        // target_channel = 0 is invalid.
        var payload = """{"target_channel": 0, "source_type": "UVC", "source_url": null, "pip_settings": null}""";

        var response = await client.PostAsync(
            "/api/v1/config",
            new StringContent(payload, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(configService.Received);
    }

    [Fact]
    public async Task GetSources_ReturnsSnakeCaseJsonWithStringEnums()
    {
        var sources = new[]
        {
            new SourceInfo(1, "Cam 1", SourceProtocol.Uvc, "1920x1080", SourceStatus.Connected),
            new SourceInfo(2, "Phone", SourceProtocol.Srt, null, SourceStatus.Disconnected),
        };
        var sourceManager = new FakeInputSourceManager(sources);
        using var host = await TestWebHostFactory.CreateAsync(new FakeSwitcherConfigService(), sourceManager, new RecordingControllerInputSink());
        using var client = host.GetTestClient();

        var response = await client.GetAsync("/api/v1/sources");
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal(2, root.GetArrayLength());
        Assert.Equal(1, root[0].GetProperty("channel").GetInt32());
        Assert.Equal("Cam 1", root[0].GetProperty("name").GetString());
        Assert.Equal("UVC", root[0].GetProperty("protocol").GetString());
        Assert.Equal("1920x1080", root[0].GetProperty("resolution").GetString());
        Assert.Equal("Connected", root[0].GetProperty("status").GetString());
        Assert.Equal("SRT", root[1].GetProperty("protocol").GetString());
    }

    [Fact]
    public async Task PostConfig_DeserializesViaMinimalApiModelBinding()
    {
        var configService = new FakeSwitcherConfigService();
        using var host = await TestWebHostFactory.CreateAsync(configService, new FakeInputSourceManager([]), new RecordingControllerInputSink());
        using var client = host.GetTestClient();

        var request = new ConfigChangeRequest(3, SourceProtocol.Uvc, null, null);
        var response = await client.PostAsJsonAsync("/api/v1/config", request, ProtocolJsonOptions.Default);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(3, Assert.Single(configService.Received).TargetChannel);
    }
}
