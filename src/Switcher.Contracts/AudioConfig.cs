using System.Text.Json.Serialization;

namespace Switcher.Contracts;

/// <summary>
/// How a source's audio reaches the program buses (docs/specs/00-system-overview.md §4.2).
/// A source is added with its audio already attached — there is no separate "audio source" to create.
/// </summary>
public enum SourceAudioMode
{
    /// <summary>Never audible. The source is video-only.</summary>
    [JsonStringEnumMemberName("OFF")]
    Off,

    /// <summary>Audible on every bus regardless of what is on air — a presenter mic, background music.</summary>
    [JsonStringEnumMemberName("ON")]
    On,

    /// <summary>Audio follows video: audible on a bus only while the source is live on that bus.</summary>
    [JsonStringEnumMemberName("AFV")]
    Afv,
}

/// <summary>
/// Routes one program bus to one audio output device.
/// <para>
/// A bus may appear more than once to feed several devices at the same time (front of house plus a
/// recorder, say), and each bus is independent — which is why this cannot use libobs' monitoring, whose
/// output device is a single process-wide setting.
/// </para>
/// An empty <paramref name="DeviceId"/> means the system default endpoint. HDMI needs no special case:
/// an HDMI sink is an ordinary render endpoint, so routing a bus to it is the same operation.
/// </summary>
public sealed record AudioOutputAssignment(ProgramBus Bus, string DeviceId, string? DeviceName = null);

/// <summary>Request body for PUT /api/v1/audio/outputs. Replaces the whole routing table.</summary>
public sealed record AudioOutputsRequest(IReadOnlyList<AudioOutputAssignment> Outputs);

/// <summary>An audio render endpoint the machine can play to.</summary>
public sealed record AudioDeviceInfo(string Id, string Name, bool IsDefault);
