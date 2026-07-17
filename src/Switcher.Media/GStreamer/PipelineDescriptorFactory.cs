using Switcher.Contracts;

namespace Switcher.Media.GStreamer;

/// <summary>
/// Builds the <c>gst-launch</c>-style pipeline description for a single input channel, per protocol
/// (docs/specs/pc-switcher-app.md §2.1). Pure string composition with no GStreamer dependency, so it
/// can be unit tested without a native GStreamer runtime.
/// </summary>
internal static class PipelineDescriptorFactory
{
    /// <summary>Target output caps for every source: constant format simplifies downstream GPU upload.</summary>
    private const string DecodeAndSinkTail =
        "videoconvert ! video/x-raw,format=BGRA ! " +
        "appsink name=sink emit-signals=true sync=false max-buffers=1 drop=true";

    /// <summary>SRT recommended latency (docs/specs/pc-switcher-app.md §4: 20-50ms), fixed at the midpoint.</summary>
    private const int SrtLatencyMilliseconds = 40;

    public static string Build(SourceProtocol protocol, string? sourceUrl) => protocol switch
    {
        SourceProtocol.Uvc => BuildUvc(sourceUrl),
        SourceProtocol.Ndi => BuildNdi(sourceUrl),
        SourceProtocol.Srt => BuildSrt(sourceUrl),
        _ => throw new ArgumentOutOfRangeException(nameof(protocol), protocol, "Unsupported source protocol."),
    };

    // mfvideosrc (Media Foundation) is the primary UVC source element on modern Windows; ksvideosrc
    // (DirectShow) is the legacy fallback mentioned by the spec for older capture drivers.
    private static string BuildUvc(string? sourceUrl)
    {
        var deviceProperty = string.IsNullOrWhiteSpace(sourceUrl) ? string.Empty : $"device-path=\"{sourceUrl}\" ";
        return $"mfvideosrc {deviceProperty}! {DecodeAndSinkTail}";
    }

    private static string BuildNdi(string? sourceUrl)
    {
        var nameProperty = string.IsNullOrWhiteSpace(sourceUrl) ? string.Empty : $"ndi-name=\"{sourceUrl}\" ";
        return $"ndisrc {nameProperty}! {DecodeAndSinkTail}";
    }

    // Listener mode on the fixed protocol port (Switcher.Contracts.ProtocolConstants.SrtListenPort);
    // sourceUrl may override the full SRT URI (e.g. for tests against a non-default port).
    private static string BuildSrt(string? sourceUrl)
    {
        var uri = string.IsNullOrWhiteSpace(sourceUrl)
            ? $"srt://:{ProtocolConstants.SrtListenPort}?mode=listener&latency={SrtLatencyMilliseconds}"
            : sourceUrl;
        return $"srtsrc uri=\"{uri}\" ! {DecodeAndSinkTail}";
    }
}
