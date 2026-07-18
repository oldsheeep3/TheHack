using Switcher.Contracts;

namespace Switcher.Hid.Input;

/// <summary>
/// Deadbands/decimates VR (potentiometer) readings so downstream consumers only see meaningful
/// changes rather than every frame's analog jitter (docs/specs/pc-switcher-app.md §2.6: "VRは適度な
/// デッドバンド/間引き"). Stateful across calls to <see cref="Process"/> — one instance tracks one
/// input stream. The default deadband (3) matches the firmware's <c>VR_DEADBAND_DELTA</c>
/// (firmware/pico2w-controller/include/config.h), since the same jitter characteristics apply.
/// </summary>
public sealed class VrFilter
{
    private const byte DefaultDeadband = 3;

    private readonly byte _deadband;
    private readonly byte?[] _lastReportedSrc1 = new byte?[ProtocolConstants.MaxModules];
    private readonly byte?[] _lastReportedSrc2 = new byte?[ProtocolConstants.MaxModules];

    public VrFilter(byte deadband = DefaultDeadband)
    {
        _deadband = deadband;
    }

    /// <summary>Returns the VR channels whose value moved by at least the deadband since the last
    /// reported value for that channel (or that have never been reported yet).</summary>
    public IReadOnlyList<VrChangedEvent> Process(HidInputReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        List<VrChangedEvent>? changes = null;
        for (var i = 0; i < report.Vrs.Count; i++)
        {
            var vr = report.Vrs[i];
            TryEmit(ref changes, i, VrChannel.Src1, vr.VrSrc1, _lastReportedSrc1);
            TryEmit(ref changes, i, VrChannel.Src2, vr.VrSrc2, _lastReportedSrc2);
        }

        return changes ?? (IReadOnlyList<VrChangedEvent>)[];
    }

    private void TryEmit(ref List<VrChangedEvent>? changes, int moduleIndex, VrChannel channel, byte value, byte?[] lastReported)
    {
        var last = lastReported[moduleIndex];
        if (last is not null && Math.Abs(value - last.Value) < _deadband)
        {
            return;
        }

        lastReported[moduleIndex] = value;
        changes ??= [];
        changes.Add(new VrChangedEvent(moduleIndex, channel, value));
    }
}
