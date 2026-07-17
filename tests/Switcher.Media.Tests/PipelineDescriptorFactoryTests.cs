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
}
