namespace Switcher.Contracts;

/// <summary>
/// Builds the <c>srt://</c> URL this PC opens for an <see cref="SourceType.Srt"/> source.
/// <para>
/// <see cref="SrtConfig"/> carries no mode field: the Listener/Caller choice belongs in the URL query
/// (docs/specs/00-system-overview.md §4.3), and FFmpeg's libsrt defaults to <c>caller</c> when it is
/// absent. That default is what makes a Listener setup look dead - the PC dials *out* instead of binding
/// the port, so the ATEM/OBS at the other end finds nobody to connect to and the source stays black.
/// </para>
/// <para>
/// A Listener also has to bind a wildcard address rather than this PC's own LAN IP. The LAN IP is what
/// the *sender* dials (<see cref="SrtSetupInfo.RecommendedUrl"/>); pointing the receiving end at it would
/// have the PC call itself.
/// </para>
/// </summary>
public static class SrtUrl
{
    /// <summary>
    /// Normalizes <paramref name="url"/> for the selected mode. An explicit <c>mode=</c> already in the
    /// query is the operator's own choice and is preserved, as is any other query parameter
    /// (<c>passphrase</c>, <c>streamid</c>, ...). A Listener keeps only the port from
    /// <paramref name="url"/>; an empty <paramref name="url"/> binds
    /// <see cref="ProtocolConstants.SrtListenPort"/>.
    /// </summary>
    public static string ForMode(string? url, bool listener)
    {
        var text = (url ?? string.Empty).Trim();
        if (text.Length > 0 && !text.Contains("://", StringComparison.Ordinal))
        {
            text = "srt://" + text;   // "192.168.1.50:9000" typed bare
        }

        var mark = text.IndexOf('?', StringComparison.Ordinal);
        var authority = mark >= 0 ? text[..mark] : text;
        var query = mark >= 0 ? text[(mark + 1)..] : string.Empty;

        var port = Uri.TryCreate(authority, UriKind.Absolute, out var parsed) && parsed.Port > 0
            ? parsed.Port
            : ProtocolConstants.SrtListenPort;

        if (listener)
        {
            // 0.0.0.0 accepts the push on every NIC, so the sender may use any of the host candidates.
            authority = $"srt://0.0.0.0:{port}";
        }

        var hasMode = query.Split('&').Any(p => p.StartsWith("mode=", StringComparison.OrdinalIgnoreCase));
        if (!hasMode)
        {
            var mode = listener ? "mode=listener" : "mode=caller";
            query = query.Length == 0 ? mode : $"{query}&{mode}";
        }

        return $"{authority}?{query}";
    }
}
