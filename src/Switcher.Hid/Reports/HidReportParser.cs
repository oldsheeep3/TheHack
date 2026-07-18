using Switcher.Contracts;

namespace Switcher.Hid.Reports;

/// <summary>
/// Pure byte-layout codec for the input/output HID report bodies (docs/specs/00-system-overview.md
/// §4.1, mirrored by the firmware's <c>state_agg.c</c>/<c>backlight_codec.c</c>). Operates on report
/// bodies only — the leading Report ID byte (<see cref="ProtocolConstants.HidInputReportId"/> /
/// <see cref="ProtocolConstants.HidOutputReportId"/>) is a device-transport concern handled by the
/// callers in <see cref="Switcher.Hid.Devices"/>, not by this parser.
/// </summary>
public static class HidReportParser
{
    private const int SwitchBitsPgm1Src1 = 1 << 0;
    private const int SwitchBitsPgm1Src2 = 1 << 1;
    private const int SwitchBitsPgm2Src1 = 1 << 2;
    private const int SwitchBitsPgm2Src2 = 1 << 3;

    /// <summary>Length of the input report body: module_present(1) + SW×MAX_MODULES + VR×2×MAX_MODULES + seq(1).</summary>
    public static int InputReportLength { get; } =
        1 + ProtocolConstants.MaxModules + 2 * ProtocolConstants.MaxModules + 1;

    /// <summary>Length of the output report body: module_index(1) + 4 backlights × RGB(3).</summary>
    public static int OutputReportLength { get; } =
        1 + ProtocolConstants.BacklightsPerModule * 3;

    /// <summary>Parses an input report body (Report ID <see cref="ProtocolConstants.HidInputReportId"/>
    /// already stripped) into a <see cref="HidInputReport"/>, per the fixed layout in
    /// docs/specs/00-system-overview.md §4.1.</summary>
    public static HidInputReport ParseInput(ReadOnlySpan<byte> data)
    {
        if (data.Length < InputReportLength)
        {
            throw new ArgumentException(
                $"Input report body ({data.Length} bytes) is shorter than the expected {InputReportLength} bytes.",
                nameof(data));
        }

        var modulePresent = data[0];

        var switches = new ModuleSwitchState[ProtocolConstants.MaxModules];
        for (var i = 0; i < ProtocolConstants.MaxModules; i++)
        {
            var raw = data[1 + i];
            switches[i] = new ModuleSwitchState(
                Pgm1Src1: (raw & SwitchBitsPgm1Src1) != 0,
                Pgm1Src2: (raw & SwitchBitsPgm1Src2) != 0,
                Pgm2Src1: (raw & SwitchBitsPgm2Src1) != 0,
                Pgm2Src2: (raw & SwitchBitsPgm2Src2) != 0);
        }

        var vrOffset = 1 + ProtocolConstants.MaxModules;
        var vrs = new ModuleVrState[ProtocolConstants.MaxModules];
        for (var i = 0; i < ProtocolConstants.MaxModules; i++)
        {
            vrs[i] = new ModuleVrState(
                VrSrc1: data[vrOffset + i * 2],
                VrSrc2: data[vrOffset + i * 2 + 1]);
        }

        var seq = data[InputReportLength - 1];

        return new HidInputReport(modulePresent, switches, vrs, seq);
    }

    /// <summary>Serializes an output report body (module_index + 4× RGB backlights, RGB order — the
    /// GRB conversion for SK6812 is done by the module firmware, not here) per
    /// docs/specs/00-system-overview.md §4.1/§4.5. Does not include the leading Report ID byte.</summary>
    public static byte[] SerializeOutput(HidOutputReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        if (report.Colors.Count != ProtocolConstants.BacklightsPerModule)
        {
            throw new ArgumentException(
                $"Output report must have exactly {ProtocolConstants.BacklightsPerModule} backlight colors, got {report.Colors.Count}.",
                nameof(report));
        }

        var data = new byte[OutputReportLength];
        data[0] = report.ModuleIndex;
        for (var i = 0; i < ProtocolConstants.BacklightsPerModule; i++)
        {
            var color = report.Colors[i];
            data[1 + i * 3] = color.R;
            data[1 + i * 3 + 1] = color.G;
            data[1 + i * 3 + 2] = color.B;
        }

        return data;
    }
}
