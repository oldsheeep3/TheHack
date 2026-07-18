using Switcher.Contracts;
using Switcher.Hid.Reports;

namespace Switcher.Hid.Tests;

public sealed class HidReportParserTests
{
    [Fact]
    public void InputReportLength_MatchesFixedLayout()
    {
        // module_present(1) + SW×8 + VR×2×8 + seq(1)
        Assert.Equal(1 + 8 + 16 + 1, HidReportParser.InputReportLength);
    }

    [Fact]
    public void OutputReportLength_MatchesFixedLayout()
    {
        // module_index(1) + 4 backlights × RGB(3)
        Assert.Equal(1 + 12, HidReportParser.OutputReportLength);
    }

    [Fact]
    public void ParseInput_DecodesModulePresentBitmap()
    {
        var data = new byte[HidReportParser.InputReportLength];
        data[0] = 0b0000_0101; // modules 0 and 2 present

        var report = HidReportParser.ParseInput(data);

        Assert.Equal(0b0000_0101, report.ModulePresent);
    }

    [Theory]
    [InlineData(0b0000_0001, true, false, false, false)]
    [InlineData(0b0000_0010, false, true, false, false)]
    [InlineData(0b0000_0100, false, false, true, false)]
    [InlineData(0b0000_1000, false, false, false, true)]
    [InlineData(0b0000_1111, true, true, true, true)]
    public void ParseInput_DecodesSwitchBitsInDocumentedBitOrder(
        byte raw, bool pgm1Src1, bool pgm1Src2, bool pgm2Src1, bool pgm2Src2)
    {
        var data = new byte[HidReportParser.InputReportLength];
        data[1] = raw; // module 0's SW byte

        var report = HidReportParser.ParseInput(data);

        var sw = report.Switches[0];
        Assert.Equal(pgm1Src1, sw.Pgm1Src1);
        Assert.Equal(pgm1Src2, sw.Pgm1Src2);
        Assert.Equal(pgm2Src1, sw.Pgm2Src1);
        Assert.Equal(pgm2Src2, sw.Pgm2Src2);
    }

    [Fact]
    public void ParseInput_DecodesVrValuesAtDocumentedOffset()
    {
        var data = new byte[HidReportParser.InputReportLength];
        // VR block starts right after module_present(1) + SW×8, 2 bytes per module.
        var vrOffset = 1 + ProtocolConstants.MaxModules;
        data[vrOffset + 3 * 2] = 10; // module 3, VR_SRC1
        data[vrOffset + 3 * 2 + 1] = 200; // module 3, VR_SRC2

        var report = HidReportParser.ParseInput(data);

        Assert.Equal(10, report.Vrs[3].VrSrc1);
        Assert.Equal(200, report.Vrs[3].VrSrc2);
        Assert.Equal(0, report.Vrs[0].VrSrc1);
    }

    [Fact]
    public void ParseInput_DecodesSeqAtLastByte()
    {
        var data = new byte[HidReportParser.InputReportLength];
        data[^1] = 42;

        var report = HidReportParser.ParseInput(data);

        Assert.Equal(42, report.Seq);
    }

    [Fact]
    public void ParseInput_TooShort_Throws()
    {
        var data = new byte[HidReportParser.InputReportLength - 1];

        Assert.Throws<ArgumentException>(() => HidReportParser.ParseInput(data));
    }

    [Fact]
    public void ParseInput_ExtraTrailingBytes_AreIgnored()
    {
        var data = new byte[HidReportParser.InputReportLength + 5];
        data[^6] = 7; // last byte of the report body proper (before the extra padding)

        var report = HidReportParser.ParseInput(data);

        Assert.Equal(7, report.Seq);
    }

    [Fact]
    public void SerializeOutput_WritesModuleIndexAndRgbInOrder()
    {
        var report = new HidOutputReport(
            ModuleIndex: 3,
            Colors:
            [
                new BacklightColor(1, 2, 3),
                new BacklightColor(4, 5, 6),
                new BacklightColor(7, 8, 9),
                new BacklightColor(10, 11, 12),
            ]);

        var data = HidReportParser.SerializeOutput(report);

        Assert.Equal(HidReportParser.OutputReportLength, data.Length);
        Assert.Equal<byte[]>([3, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12], data);
    }

    [Fact]
    public void SerializeOutput_WrongColorCount_Throws()
    {
        var report = new HidOutputReport(0, [new BacklightColor(0, 0, 0)]);

        Assert.Throws<ArgumentException>(() => HidReportParser.SerializeOutput(report));
    }

    [Fact]
    public void ParseInput_SerializeOutput_RoundTripsThroughModuleAndSwitchLayout()
    {
        var data = new byte[HidReportParser.InputReportLength];
        data[0] = 0b1010_1010;
        for (var i = 0; i < ProtocolConstants.MaxModules; i++)
        {
            data[1 + i] = (byte)(i % 16);
        }

        var vrOffset = 1 + ProtocolConstants.MaxModules;
        for (var i = 0; i < ProtocolConstants.MaxModules; i++)
        {
            data[vrOffset + i * 2] = (byte)(i * 10);
            data[vrOffset + i * 2 + 1] = (byte)(255 - i);
        }

        data[^1] = 99;

        var report = HidReportParser.ParseInput(data);

        Assert.Equal(0b1010_1010, report.ModulePresent);
        Assert.Equal(99, report.Seq);
        Assert.Equal(ProtocolConstants.MaxModules, report.Switches.Count);
        Assert.Equal(ProtocolConstants.MaxModules, report.Vrs.Count);
        Assert.Equal(70, report.Vrs[7].VrSrc1);
        Assert.Equal(248, report.Vrs[7].VrSrc2);
    }
}
