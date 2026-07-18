using Switcher.Contracts;

namespace Switcher.Hid.Input;

/// <summary>
/// Detects switch press/release edges from the state difference between consecutive
/// <see cref="HidInputReport"/>s (docs/specs/pc-switcher-app.md §2.6: "押下エッジは状態差分で検出").
/// Stateful across calls to <see cref="Process"/> — one instance tracks one input stream.
/// </summary>
public sealed class SwitchEdgeDetector
{
    private HidInputReport? _previous;

    /// <summary>Compares <paramref name="report"/> against the previously processed report and
    /// returns the edges observed on modules currently marked present. The first call (no prior
    /// report) never produces edges, since there is nothing to diff against.</summary>
    public IReadOnlyList<SwitchEdgeEvent> Process(HidInputReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        var edges = DetectEdges(_previous, report);
        _previous = report;
        return edges;
    }

    private static IReadOnlyList<SwitchEdgeEvent> DetectEdges(HidInputReport? previous, HidInputReport current)
    {
        if (previous is null)
        {
            return [];
        }

        List<SwitchEdgeEvent>? edges = null;
        for (var i = 0; i < current.Switches.Count; i++)
        {
            if (!IsModulePresent(current, i))
            {
                continue;
            }

            var prevSwitch = previous.Switches[i];
            var currSwitch = current.Switches[i];

            AddIfChanged(ref edges, i, SwitchId.Pgm1Src1, prevSwitch.Pgm1Src1, currSwitch.Pgm1Src1);
            AddIfChanged(ref edges, i, SwitchId.Pgm1Src2, prevSwitch.Pgm1Src2, currSwitch.Pgm1Src2);
            AddIfChanged(ref edges, i, SwitchId.Pgm2Src1, prevSwitch.Pgm2Src1, currSwitch.Pgm2Src1);
            AddIfChanged(ref edges, i, SwitchId.Pgm2Src2, prevSwitch.Pgm2Src2, currSwitch.Pgm2Src2);
        }

        return edges ?? (IReadOnlyList<SwitchEdgeEvent>)[];
    }

    private static bool IsModulePresent(HidInputReport report, int moduleIndex) =>
        (report.ModulePresent & (1 << moduleIndex)) != 0;

    private static void AddIfChanged(ref List<SwitchEdgeEvent>? edges, int moduleIndex, SwitchId switchId, bool previous, bool current)
    {
        if (previous == current)
        {
            return;
        }

        edges ??= [];
        edges.Add(new SwitchEdgeEvent(moduleIndex, switchId, IsRising: current));
    }
}
