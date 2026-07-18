using Switcher.Contracts;
using Switcher.Media.GStreamer;

namespace Switcher.Media.Tests;

public class PipelineDescriptorFactoryTests
{
    [Fact]
    public void Build_Uvc_UsesMediaFoundationSourceAndDevicePath()
    {
        var description = PipelineDescriptorFactory.Build(SourceProtocol.Uvc, "usb#vid_1234");

        Assert.Contains("mfvideosrc", description);
        Assert.Contains("device-path=\"usb#vid_1234\"", description);
        Assert.Contains("appsink name=sink", description);
    }

    [Fact]
    public void Build_Uvc_WithoutSourceUrl_OmitsDeviceProperty()
    {
        var description = PipelineDescriptorFactory.Build(SourceProtocol.Uvc, null);

        Assert.Contains("mfvideosrc", description);
        Assert.DoesNotContain("device-path", description);
    }

    [Fact]
    public void Build_Ndi_UsesNdiSourceName()
    {
        var description = PipelineDescriptorFactory.Build(SourceProtocol.Ndi, "CAM-1");

        Assert.Contains("ndisrc", description);
        Assert.Contains("ndi-name=\"CAM-1\"", description);
    }

    [Fact]
    public void Build_Srt_DefaultsToListenerModeOnFixedPort()
    {
        var description = PipelineDescriptorFactory.Build(SourceProtocol.Srt, null);

        Assert.Contains("srtsrc", description);
        Assert.Contains($":{ProtocolConstants.SrtListenPort}", description);
        Assert.Contains("mode=listener", description);
    }

    [Fact]
    public void Build_Srt_WithExplicitUri_OverridesDefault()
    {
        var description = PipelineDescriptorFactory.Build(SourceProtocol.Srt, "srt://:9500?mode=listener&latency=20");

        Assert.Contains("srt://:9500", description);
    }

    [Fact]
    public void Build_AllProtocols_EndInLowLatencyAppsink()
    {
        foreach (var protocol in new[] { SourceProtocol.Uvc, SourceProtocol.Ndi, SourceProtocol.Srt })
        {
            var description = PipelineDescriptorFactory.Build(protocol, null);
            Assert.Contains("sync=false", description);
            Assert.Contains("max-buffers=1", description);
            Assert.Contains("drop=true", description);
        }
    }

    [Fact]
    public void Build_SourceDefinition_Ndi_UsesSourceName()
    {
        var definition = new SourceDefinition("src-1", "Cam 1", SourceType.Ndi, new NdiConfig("CAM-1"), null, null);

        var description = PipelineDescriptorFactory.Build(definition);

        Assert.Contains("ndisrc", description);
        Assert.Contains("ndi-name=\"CAM-1\"", description);
    }

    [Fact]
    public void Build_SourceDefinition_Webcam_MapsToUvcWithDeviceId()
    {
        var definition = new SourceDefinition("src-2", "Webcam", SourceType.Webcam, null, new WebcamConfig("usb#vid_1234", "MJPG"), null);

        var description = PipelineDescriptorFactory.Build(definition);

        Assert.Contains("mfvideosrc", description);
        Assert.Contains("device-path=\"usb#vid_1234\"", description);
    }

    [Fact]
    public void Build_SourceDefinition_Srt_WithoutUrl_UsesListenerModeWithConfiguredLatency()
    {
        var definition = new SourceDefinition("src-3", "ATEM", SourceType.Srt, null, null, new SrtConfig("", 25));

        var description = PipelineDescriptorFactory.Build(definition);

        Assert.Contains($":{ProtocolConstants.SrtListenPort}", description);
        Assert.Contains("mode=listener", description);
        Assert.Contains("latency=25", description);
    }

    [Fact]
    public void Build_SourceDefinition_Srt_WithUrl_UsesCallerModeWithConfiguredLatency()
    {
        var definition = new SourceDefinition("src-4", "Remote", SourceType.Srt, null, null, new SrtConfig("srt://10.0.0.5:9001", 30));

        var description = PipelineDescriptorFactory.Build(definition);

        Assert.Contains("srt://10.0.0.5:9001", description);
        Assert.Contains("mode=caller", description);
        Assert.Contains("latency=30", description);
    }
}
