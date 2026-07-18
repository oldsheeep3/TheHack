using Switcher.Contracts;
using Switcher.Hid.Input;

namespace Switcher.Hid.Tests;

public sealed class SwitchEdgeDetectorTests
{
    private static HidInputReport Report(byte modulePresent, params ModuleSwitchState[] switches)
    {
        var padded = new ModuleSwitchState[ProtocolConstants.MaxModules];
        for (var i = 0; i < padded.Length; i++)
        {
            padded[i] = i < switches.Length ? switches[i] : new ModuleSwitchState(false, false, false, false);
        }

        var vrs = new ModuleVrState[ProtocolConstants.MaxModules];
        Array.Fill(vrs, new ModuleVrState(0, 0));

        return new HidInputReport(modulePresent, padded, vrs, Seq: 0);
    }

    [Fact]
    public void Process_FirstReport_ProducesNoEdges()
    {
        var detector = new SwitchEdgeDetector();
        var report = Report(0b1, new ModuleSwitchState(true, false, false, false));

        var edges = detector.Process(report);

        Assert.Empty(edges);
    }

    [Fact]
    public void Process_SwitchPressed_ProducesRisingEdge()
    {
        var detector = new SwitchEdgeDetector();
        detector.Process(Report(0b1, new ModuleSwitchState(false, false, false, false)));

        var edges = detector.Process(Report(0b1, new ModuleSwitchState(true, false, false, false)));

        var edge = Assert.Single(edges);
        Assert.Equal(0, edge.ModuleIndex);
        Assert.Equal(SwitchId.Pgm1Src1, edge.Switch);
        Assert.True(edge.IsRising);
    }

    [Fact]
    public void Process_SwitchReleased_ProducesFallingEdge()
    {
        var detector = new SwitchEdgeDetector();
        detector.Process(Report(0b1, new ModuleSwitchState(true, false, false, false)));

        var edges = detector.Process(Report(0b1, new ModuleSwitchState(false, false, false, false)));

        var edge = Assert.Single(edges);
        Assert.False(edge.IsRising);
    }

    [Fact]
    public void Process_MultipleSwitchesChange_ProducesOneEdgePerSwitch()
    {
        var detector = new SwitchEdgeDetector();
        detector.Process(Report(0b1, new ModuleSwitchState(false, false, false, false)));

        var edges = detector.Process(Report(0b1, new ModuleSwitchState(true, false, true, false)));

        Assert.Equal(2, edges.Count);
        Assert.Contains(edges, e => e.Switch == SwitchId.Pgm1Src1 && e.IsRising);
        Assert.Contains(edges, e => e.Switch == SwitchId.Pgm2Src1 && e.IsRising);
    }

    [Fact]
    public void Process_ModuleNotPresent_IgnoresItsSwitchChanges()
    {
        var detector = new SwitchEdgeDetector();
        detector.Process(Report(0b1, new ModuleSwitchState(false, false, false, false)));

        // Module 0 is no longer marked present, even though its raw SW byte changed.
        var edges = detector.Process(Report(0b0, new ModuleSwitchState(true, false, false, false)));

        Assert.Empty(edges);
    }

    [Fact]
    public void Process_NoChange_ProducesNoEdges()
    {
        var detector = new SwitchEdgeDetector();
        var report = Report(0b1, new ModuleSwitchState(true, true, false, false));
        detector.Process(report);

        var edges = detector.Process(report);

        Assert.Empty(edges);
    }
}
