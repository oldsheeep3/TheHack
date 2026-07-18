using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Hosting;
using Switcher.Contracts;

namespace Switcher.Web.Tests;

public class DevicesEndpointTests
{
    private static async Task<IHostAndClient> CreateAsync(FakeDeviceQueryService deviceQuery)
    {
        var host = await TestWebHostFactory.CreateAsync(
            new FakeSwitcherConfigService(),
            new FakeInputSourceManager([]),
            new RecordingControllerInputSink(),
            deviceQuery);
        return new IHostAndClient(host, host.GetTestClient());
    }

    [Fact]
    public async Task GetDevices_Webcam_ReturnsEnumeratedDevices()
    {
        var deviceQuery = new FakeDeviceQueryService(new Dictionary<DeviceQueryType, IReadOnlyList<DeviceInfo>>
        {
            [DeviceQueryType.Webcam] = [new DeviceInfo("cam0", "USB Camera", ["1920x1080@30", "1280x720@60"])],
        });
        using var hc = await CreateAsync(deviceQuery);

        var response = await hc.Client.GetAsync("/api/v1/devices/webcam");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var devices = await response.Content.ReadFromJsonAsync<List<DeviceInfo>>(ProtocolJsonOptions.Default);
        Assert.NotNull(devices);
        var device = Assert.Single(devices!);
        Assert.Equal("cam0", device.Id);
        Assert.Equal(DeviceQueryType.Webcam, Assert.Single(deviceQuery.EnumerateCalls));
    }

    [Fact]
    public async Task GetDevices_Ndi_ReturnsEnumeratedDevices()
    {
        var deviceQuery = new FakeDeviceQueryService(new Dictionary<DeviceQueryType, IReadOnlyList<DeviceInfo>>
        {
            [DeviceQueryType.Ndi] = [new DeviceInfo("STUDIO (Cam1)", "STUDIO (Cam1)", null)],
        });
        using var hc = await CreateAsync(deviceQuery);

        var response = await hc.Client.GetAsync("/api/v1/devices/ndi");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var devices = await response.Content.ReadFromJsonAsync<List<DeviceInfo>>(ProtocolJsonOptions.Default);
        Assert.Equal("STUDIO (Cam1)", Assert.Single(devices!).Id);
        Assert.Equal(DeviceQueryType.Ndi, Assert.Single(deviceQuery.EnumerateCalls));
    }

    [Fact]
    public async Task GetDevices_WithNoDevices_ReturnsEmptyArrayAnd200()
    {
        var deviceQuery = new FakeDeviceQueryService();
        using var hc = await CreateAsync(deviceQuery);

        var response = await hc.Client.GetAsync("/api/v1/devices/webcam");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var devices = await response.Content.ReadFromJsonAsync<List<DeviceInfo>>(ProtocolJsonOptions.Default);
        Assert.Empty(devices!);
    }

    [Fact]
    public async Task GetDevices_UnknownType_ReturnsBadRequestWithoutEnumerating()
    {
        var deviceQuery = new FakeDeviceQueryService();
        using var hc = await CreateAsync(deviceQuery);

        var response = await hc.Client.GetAsync("/api/v1/devices/bogus");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(deviceQuery.EnumerateCalls);
    }

    [Fact]
    public async Task GetDevices_Srt_ReturnsBadRequestBecauseNotEnumerable()
    {
        var deviceQuery = new FakeDeviceQueryService();
        using var hc = await CreateAsync(deviceQuery);

        var response = await hc.Client.GetAsync("/api/v1/devices/srt");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(deviceQuery.EnumerateCalls);
    }

    [Fact]
    public async Task GetSrtSetup_ReturnsSetupInfoFromService()
    {
        var deviceQuery = new FakeDeviceQueryService(
            srtSetup: new SrtSetupInfo(9000, ["192.168.1.50", "10.0.0.3"], "srt://192.168.1.50:9000", 120, "Point your encoder here."));
        using var hc = await CreateAsync(deviceQuery);

        var response = await hc.Client.GetAsync("/api/v1/srt/setup");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var setup = await response.Content.ReadFromJsonAsync<SrtSetupInfo>(ProtocolJsonOptions.Default);
        Assert.NotNull(setup);
        Assert.Equal(9000, setup!.ListenerPort);
        Assert.Equal("srt://192.168.1.50:9000", setup.RecommendedUrl);
        Assert.Equal(2, setup.HostCandidates.Count);
    }

    private sealed class IHostAndClient(IHost host, HttpClient client) : IDisposable
    {
        public HttpClient Client { get; } = client;

        public void Dispose()
        {
            Client.Dispose();
            host.Dispose();
        }
    }
}
