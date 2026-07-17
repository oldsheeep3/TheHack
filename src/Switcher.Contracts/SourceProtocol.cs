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
}
