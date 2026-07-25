using System.Text.Json.Serialization;

namespace Switcher.Contracts;

/// <summary>
/// OBS的に自由追加できるソースの種別（docs/specs/00-system-overview.md §4.2）。
/// </summary>
public enum SourceType
{
    [JsonStringEnumMemberName("NDI")]
    Ndi,

    [JsonStringEnumMemberName("WEBCAM")]
    Webcam,

    [JsonStringEnumMemberName("SRT")]
    Srt,

    /// <summary>Still image from a local file (libobs <c>image_source</c>).</summary>
    [JsonStringEnumMemberName("IMAGE")]
    Image,

    /// <summary>Web page / local HTML file rendered by CEF (libobs <c>browser_source</c>, obs-browser).</summary>
    [JsonStringEnumMemberName("HTML")]
    Html,

    /// <summary>Several other sources composited into one, addressable anywhere a single source is
    /// (bus layer, multiview cell, module binding). Backed by a private libobs scene.</summary>
    [JsonStringEnumMemberName("MIX")]
    Mix,
}

public sealed record NdiConfig(string SourceName);

public sealed record WebcamConfig(string DeviceId, string? Format);

public sealed record SrtConfig(string Url, int LatencyMs);

/// <summary>A still image loaded from <paramref name="FilePath"/> (png/jpg/gif/webp/bmp).</summary>
public sealed record ImageConfig(string FilePath);

/// <summary>
/// A browser source. <paramref name="Url"/> is either an <c>http(s)://</c> address or, when
/// <paramref name="IsLocalFile"/> is set, a path to an <c>.html</c> file on disk. The page is rendered
/// off-screen at <paramref name="Width"/>×<paramref name="Height"/> and scaled into its scene item, so
/// this is the page's own layout resolution rather than its size on the canvas.
/// </summary>
public sealed record HtmlConfig(
    string Url,
    int Width = EngineDefaults.CanvasWidth,
    int Height = EngineDefaults.CanvasHeight,
    bool IsLocalFile = false,
    int Fps = 30,
    string? Css = null);

/// <summary>
/// One member of a <see cref="MixConfig"/>: another source placed at a rectangle on the mix canvas.
/// <paramref name="ZOrder"/> orders layers bottom-up, so a higher value draws on top.
/// </summary>
public sealed record MixLayer(
    string SourceId,
    [property: JsonPropertyName("x_position")] int X,
    [property: JsonPropertyName("y_position")] int Y,
    int Width,
    int Height,
    int ZOrder = 0,
    CropRect? Crop = null);

/// <summary>
/// A composite source: <paramref name="Layers"/> arranged on a
/// <paramref name="CanvasWidth"/>×<paramref name="CanvasHeight"/> canvas and exposed as one source.
/// Members are referenced by id and stay independently usable — a camera can be on a bus by itself and
/// inside a mix at the same time, because libobs opens each device once and shares it.
/// </summary>
public sealed record MixConfig(
    IReadOnlyList<MixLayer> Layers,
    int CanvasWidth = EngineDefaults.CanvasWidth,
    int CanvasHeight = EngineDefaults.CanvasHeight);

/// <summary>
/// Request body for POST /api/v1/sources (docs/specs/00-system-overview.md §4.2).
/// Exactly one of the per-type config members is expected to be set, matching <see cref="Type"/>.
/// <see cref="AudioMode"/> defaults to audio-follows-video, which is what an operator expects of a
/// camera: you hear it when it is on air.
/// </summary>
public sealed record SourceDefinition(
    string Id,
    string Name,
    SourceType Type,
    NdiConfig? Ndi,
    WebcamConfig? Webcam,
    SrtConfig? Srt,
    ImageConfig? Image = null,
    HtmlConfig? Html = null,
    MixConfig? Mix = null,
    SourceAudioMode AudioMode = SourceAudioMode.Afv);
