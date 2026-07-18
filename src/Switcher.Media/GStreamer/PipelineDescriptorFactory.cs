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

    /// <summary>Builds the pipeline description for an OBS-like <see cref="SourceDefinition"/>
    /// (docs/specs/pc-switcher-app.md §2.1) by mapping it onto the existing protocol/URL pipeline
    /// (<see cref="Build(SourceProtocol,string?)"/>) rather than introducing a parallel build path.</summary>
    public static string Build(SourceDefinition definition)
    {
        var (protocol, sourceUrl) = MapToProtocolAndUrl(definition);
        return Build(protocol, sourceUrl);
    }

    /// <summary>Maps a <see cref="SourceType"/> and its type-specific config onto the
    /// (<see cref="SourceProtocol"/>, source URL) pair the existing pipeline builders expect.
    /// WebCam's <see cref="WebcamConfig.Format"/> is not yet wired in, matching the existing UVC
    /// pipeline which has no format-selection capability either.</summary>
    internal static (SourceProtocol Protocol, string? SourceUrl) MapToProtocolAndUrl(SourceDefinition definition) => definition.Type switch
    {
        SourceType.Ndi => (SourceProtocol.Ndi, definition.Ndi?.SourceName),
        SourceType.Webcam => (SourceProtocol.Uvc, definition.Webcam?.DeviceId),
        SourceType.Srt => (SourceProtocol.Srt, BuildSrtSourceUrl(definition.Srt)),
        _ => throw new ArgumentOutOfRangeException(nameof(definition), definition.Type, "Unsupported source type."),
    };

    // Listener mode when no explicit URL is given (default port, per docs/specs/pc-switcher-app.md
    // §2.1 "Listener（Port 9000）/Caller"), otherwise Caller mode to the given address. Both reflect
    // SrtConfig.LatencyMs instead of the protocol-level BuildSrt's fixed SrtLatencyMilliseconds.
    private static string? BuildSrtSourceUrl(SrtConfig? srt)
    {
        if (srt is null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(srt.Url))
        {
            return $"srt://:{ProtocolConstants.SrtListenPort}?mode=listener&latency={srt.LatencyMs}";
        }

        if (srt.Url.Contains("mode=", StringComparison.OrdinalIgnoreCase))
        {
            return srt.Url;
        }

        var separator = srt.Url.Contains('?') ? '&' : '?';
        return $"{srt.Url}{separator}mode=caller&latency={srt.LatencyMs}";
    }

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
