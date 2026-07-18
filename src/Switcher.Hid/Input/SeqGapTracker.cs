namespace Switcher.Hid.Input;

/// <summary>
/// Detects gaps in the input report's rotating <c>seq</c> byte, indicating one or more dropped
/// frames between reads (docs/specs/00-system-overview.md §4.1: "seq（0-255ローテート, 取りこぼし
/// 検出用）"; docs/tasks/agent-A2-004-hid-io.md: "取りこぼしを検出しログ/フラグ化"). Stateful across
/// calls to <see cref="Update"/> — one instance tracks one input stream.
/// </summary>
public sealed class SeqGapTracker
{
    private byte? _lastSeq;

    /// <summary>Records <paramref name="seq"/> and returns true if it did not immediately follow the
    /// previously recorded value (mod 256). The first call never reports a gap.</summary>
    public bool Update(byte seq)
    {
        var hadGap = _lastSeq is not null && unchecked((byte)(_lastSeq.Value + 1)) != seq;
        _lastSeq = seq;
        return hadGap;
    }
}
