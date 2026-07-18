namespace Switcher.Contracts;

/// <summary>
/// Guidance for configuring an incoming SRT stream toward this PC
/// (docs/specs/multiview-output-revision.md §2.7).
/// <para>
/// <see cref="HostCandidates"/> lists the PC's LAN IPv4 addresses (one per active NIC).
/// <see cref="RecommendedUrl"/> is a ready-to-copy caller URL such as
/// <c>srt://192.168.1.50:9000</c>.
/// </para>
/// </summary>
public sealed record SrtSetupInfo(
    int ListenerPort,
    IReadOnlyList<string> HostCandidates,
    string RecommendedUrl,
    int RecommendedLatencyMs,
    string InstructionsText);
