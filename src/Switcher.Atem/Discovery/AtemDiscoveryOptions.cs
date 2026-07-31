namespace Switcher.Atem.Discovery;

/// <summary>
/// Timing budget for an <see cref="AtemDiscoveryService"/> sweep.
/// </summary>
/// <param name="ProbeInterval">Gap between outgoing hello packets. Firing a whole /24 back to back
/// overruns the send buffer and the tail of the sweep is silently dropped, so the fan-out is paced.</param>
/// <param name="Timeout">Hard ceiling on the whole sweep, so a scan can never hang the UI.</param>
/// <param name="QuietPeriod">How long to keep listening after the last thing an ATEM said. A sweep
/// ends as soon as the network goes quiet for this long, which is what makes a small LAN finish fast
/// instead of always burning the full <paramref name="Timeout"/>.</param>
public sealed record AtemDiscoveryOptions(
    TimeSpan ProbeInterval,
    TimeSpan Timeout,
    TimeSpan QuietPeriod)
{
    public static AtemDiscoveryOptions Default { get; } = new(
        ProbeInterval: TimeSpan.FromMilliseconds(2),
        Timeout: TimeSpan.FromSeconds(6),
        QuietPeriod: TimeSpan.FromMilliseconds(900));

    /// <summary>Tighter budget for checking one known address (the manual-entry "test" path).</summary>
    public static AtemDiscoveryOptions SingleHost { get; } = new(
        ProbeInterval: TimeSpan.Zero,
        Timeout: TimeSpan.FromSeconds(3),
        QuietPeriod: TimeSpan.FromMilliseconds(700));
}
