using System.Text.Json.Serialization;

namespace Switcher.Contracts;

public enum SourceProtocol
{
    [JsonStringEnumMemberName("UVC")]
    Uvc,

    [JsonStringEnumMemberName("NDI")]
    Ndi,

    [JsonStringEnumMemberName("SRT")]
    Srt,

    [JsonStringEnumMemberName("IMAGE")]
    Image,

    [JsonStringEnumMemberName("HTML")]
    Html,

    [JsonStringEnumMemberName("MIX")]
    Mix,
}
