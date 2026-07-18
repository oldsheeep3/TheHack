using Switcher.Hid.Input;

namespace Switcher.Hid.Tests;

public sealed class SeqGapTrackerTests
{
    [Fact]
    public void Update_FirstCall_NeverReportsGap()
    {
        var tracker = new SeqGapTracker();

        Assert.False(tracker.Update(200));
    }

    [Fact]
    public void Update_ConsecutiveSeq_NoGap()
    {
        var tracker = new SeqGapTracker();
        tracker.Update(10);

        Assert.False(tracker.Update(11));
    }

    [Fact]
    public void Update_SkippedSeq_ReportsGap()
    {
        var tracker = new SeqGapTracker();
        tracker.Update(10);

        Assert.True(tracker.Update(12));
    }

    [Fact]
    public void Update_SeqRolloverFrom255To0_NoGap()
    {
        var tracker = new SeqGapTracker();
        tracker.Update(255);

        Assert.False(tracker.Update(0));
    }

    [Fact]
    public void Update_GapAcrossRollover_ReportsGap()
    {
        var tracker = new SeqGapTracker();
        tracker.Update(254);

        Assert.True(tracker.Update(0));
    }
}
