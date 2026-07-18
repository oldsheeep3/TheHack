using Switcher.Contracts;
using Switcher.Hid.Input;

namespace Switcher.Hid.Tests;

public sealed class VrFilterTests
{
    private static HidInputReport Report(byte src1, byte src2, int moduleIndex = 0)
    {
        return ReportWith((moduleIndex, src1, src2));
    }

    private static HidInputReport ReportWith(params (int ModuleIndex, byte Src1, byte Src2)[] modules)
    {
        var switches = new ModuleSwitchState[ProtocolConstants.MaxModules];
        Array.Fill(switches, new ModuleSwitchState(false, false, false, false));

        var vrs = new ModuleVrState[ProtocolConstants.MaxModules];
        Array.Fill(vrs, new ModuleVrState(0, 0));
        foreach (var (moduleIndex, src1, src2) in modules)
        {
            vrs[moduleIndex] = new ModuleVrState(src1, src2);
        }

        return new HidInputReport(0, switches, vrs, Seq: 0);
    }

    [Fact]
    public void Process_FirstReport_EmitsAllChannelsAsChanged()
    {
        var filter = new VrFilter();

        var changes = filter.Process(Report(100, 50));

        Assert.Contains(changes, c => c.ModuleIndex == 0 && c.Channel == VrChannel.Src1 && c.Value == 100);
        Assert.Contains(changes, c => c.ModuleIndex == 0 && c.Channel == VrChannel.Src2 && c.Value == 50);
    }

    [Fact]
    public void Process_ChangeBelowDeadband_IsSuppressed()
    {
        var filter = new VrFilter(deadband: 5);
        filter.Process(Report(100, 100));

        var changes = filter.Process(Report(102, 100)); // delta 2 < deadband 5

        Assert.Empty(changes);
    }

    [Fact]
    public void Process_ChangeAtOrAboveDeadband_IsReported()
    {
        var filter = new VrFilter(deadband: 5);
        filter.Process(Report(100, 100));

        var changes = filter.Process(Report(105, 100)); // delta 5 == deadband

        var change = Assert.Single(changes);
        Assert.Equal(VrChannel.Src1, change.Channel);
        Assert.Equal(105, change.Value);
    }

    [Fact]
    public void Process_Src1AndSrc2_AreTrackedIndependently()
    {
        var filter = new VrFilter(deadband: 5);
        filter.Process(Report(100, 100));

        var changes = filter.Process(Report(100, 120));

        var change = Assert.Single(changes);
        Assert.Equal(VrChannel.Src2, change.Channel);
    }

    [Fact]
    public void Process_DifferentModules_AreTrackedIndependently()
    {
        var filter = new VrFilter(deadband: 5);
        filter.Process(ReportWith((0, 100, 0), (3, 0, 0)));

        // Module 0 unchanged from the baseline; only module 3 moved.
        var changes = filter.Process(ReportWith((0, 100, 0), (3, 100, 0)));

        var change = Assert.Single(changes);
        Assert.Equal(3, change.ModuleIndex);
    }
}
