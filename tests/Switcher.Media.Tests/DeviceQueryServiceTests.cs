using Microsoft.Extensions.Logging.Abstractions;
using Switcher.Contracts;
using Switcher.Media.Devices;

namespace Switcher.Media.Tests;

public class DeviceQueryServiceTests
{
    private static readonly ILocalAddressProvider NoAddresses = new FakeLocalAddressProvider(Array.Empty<string>());

    [Fact]
    public async Task EnumerateAsync_Webcam_ReturnsTheProviderDevices()
    {
        var webcams = new[]
        {
            new DeviceInfo("cam-0", "Front Camera", new[] { "1920x1080@30", "1280x720@60" }),
        };
        var service = Build(webcam: new FakeDeviceProvider(webcams));

        var result = await service.EnumerateAsync(DeviceQueryType.Webcam);

        var device = Assert.Single(result);
        Assert.Equal("cam-0", device.Id);
        Assert.Equal("Front Camera", device.Name);
        Assert.Equal(["1920x1080@30", "1280x720@60"], device.Formats!);
    }

    [Fact]
    public async Task EnumerateAsync_Ndi_UsesTheNdiProviderNotTheWebcamProvider()
    {
        var webcam = new FakeDeviceProvider([new DeviceInfo("cam", "cam", null)]);
        var ndi = new FakeDeviceProvider([new DeviceInfo("STUDIO (CAM 1)", "STUDIO (CAM 1)", null)]);
        var service = Build(webcam: webcam, ndi: ndi);

        var result = await service.EnumerateAsync(DeviceQueryType.Ndi);

        Assert.Equal("STUDIO (CAM 1)", Assert.Single(result).Name);
        Assert.Equal(0, webcam.EnumerateCount);
        Assert.Equal(1, ndi.EnumerateCount);
    }

    [Fact]
    public async Task EnumerateAsync_NdiSdkNotDetected_ReturnsEmptyRatherThanThrowing()
    {
        // The default GStreamer provider returns an empty list when no NDI plugin/SDK is present; a
        // fake with an empty result stands in for that here.
        var service = Build(ndi: new FakeDeviceProvider(Array.Empty<DeviceInfo>()));

        var result = await service.EnumerateAsync(DeviceQueryType.Ndi);

        Assert.Empty(result);
    }

    [Fact]
    public async Task EnumerateAsync_WhenTheProviderThrows_IsIsolatedToAnEmptyList()
    {
        var service = Build(webcam: new FakeDeviceProvider(@throw: true));

        var result = await service.EnumerateAsync(DeviceQueryType.Webcam);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetSrtSetupAsync_BuildsListenerGuidanceFromTheFirstLanAddress()
    {
        var service = Build(addresses: new FakeLocalAddressProvider(["192.168.1.50", "10.0.0.8"]));

        var setup = await service.GetSrtSetupAsync();

        Assert.Equal(ProtocolConstants.SrtListenPort, setup.ListenerPort);
        Assert.Equal(["192.168.1.50", "10.0.0.8"], setup.HostCandidates);
        Assert.Equal($"srt://192.168.1.50:{ProtocolConstants.SrtListenPort}", setup.RecommendedUrl);
        Assert.Equal(DeviceQueryService.RecommendedLatencyMs, setup.RecommendedLatencyMs);
        Assert.False(string.IsNullOrWhiteSpace(setup.InstructionsText));
    }

    [Fact]
    public async Task GetSrtSetupAsync_NoLanAddresses_StillReturnsAPortAndPlaceholderUrl()
    {
        var service = Build(addresses: new FakeLocalAddressProvider(Array.Empty<string>()));

        var setup = await service.GetSrtSetupAsync();

        Assert.Empty(setup.HostCandidates);
        Assert.Equal(ProtocolConstants.SrtListenPort, setup.ListenerPort);
        Assert.Contains($":{ProtocolConstants.SrtListenPort}", setup.RecommendedUrl);
    }

    [Fact]
    public async Task GetSrtSetupAsync_WhenAddressProbeThrows_IsIsolatedToNoCandidates()
    {
        var service = Build(addresses: new FakeLocalAddressProvider(Array.Empty<string>(), @throw: true));

        var setup = await service.GetSrtSetupAsync();

        Assert.Empty(setup.HostCandidates);
        Assert.Equal(ProtocolConstants.SrtListenPort, setup.ListenerPort);
    }

    private static DeviceQueryService Build(
        FakeDeviceProvider? webcam = null,
        FakeDeviceProvider? ndi = null,
        ILocalAddressProvider? addresses = null) =>
        new(
            webcam ?? new FakeDeviceProvider(Array.Empty<DeviceInfo>()),
            ndi ?? new FakeDeviceProvider(Array.Empty<DeviceInfo>()),
            addresses ?? NoAddresses,
            NullLoggerFactory.Instance);
}
