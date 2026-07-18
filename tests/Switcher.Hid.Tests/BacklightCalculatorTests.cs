using Switcher.Contracts;
using Switcher.Hid.Backlight;

namespace Switcher.Hid.Tests;

public sealed class BacklightCalculatorTests
{
    private static readonly BacklightColor Program = new(255, 0, 0);
    private static readonly BacklightColor Preview = new(0, 255, 0);
    private static readonly BacklightColor Selectable = new(24, 24, 24);
    private static readonly BacklightColor Off = new(0, 0, 0);

    private static ProgramBusesState Programs(IEnumerable<string>? pgm1 = null, IEnumerable<string>? pgm2 = null) =>
        new(
            (pgm1 ?? []).ToHashSet(),
            (pgm2 ?? []).ToHashSet());

    private static PreviewBusesState Previews(IEnumerable<string>? pvw1 = null, IEnumerable<string>? pvw2 = null) =>
        new(
            (pvw1 ?? []).ToHashSet(),
            (pvw2 ?? []).ToHashSet());

    [Fact]
    public void Compute_UnboundSlot_IsOff()
    {
        var calculator = new BacklightCalculator();
        var mapping = new ModuleMapping(0, new ModuleSourceBinding(null, "transition"), new ModuleSourceBinding(null, "opacity"));

        var reports = calculator.Compute(Programs(), Previews(), [mapping]);

        var report = Assert.Single(reports);
        Assert.All(report.Colors, c => Assert.Equal(Off, c));
    }

    [Fact]
    public void Compute_BoundButInactiveSource_IsSelectable()
    {
        var calculator = new BacklightCalculator();
        var mapping = new ModuleMapping(0, new ModuleSourceBinding("cam1", "transition"), new ModuleSourceBinding(null, "opacity"));

        var reports = calculator.Compute(Programs(), Previews(), [mapping]);

        var report = Assert.Single(reports);
        Assert.Equal(Selectable, report.Colors[0]); // Pgm1Src1
    }

    [Fact]
    public void Compute_SourceOnPgm1_LightsPgm1Src1Red()
    {
        var calculator = new BacklightCalculator();
        var mapping = new ModuleMapping(0, new ModuleSourceBinding("cam1", "transition"), new ModuleSourceBinding(null, "opacity"));

        var reports = calculator.Compute(Programs(pgm1: ["cam1"]), Previews(), [mapping]);

        var report = Assert.Single(reports);
        Assert.Equal(Program, report.Colors[0]); // Pgm1Src1
        Assert.Equal(Selectable, report.Colors[2]); // Pgm2Src1: same binding, bound but inactive on PGM2
    }

    [Fact]
    public void Compute_SourceOnPreview1_LightsPgm1Src1Green()
    {
        var calculator = new BacklightCalculator();
        var mapping = new ModuleMapping(0, new ModuleSourceBinding("cam1", "transition"), new ModuleSourceBinding(null, "opacity"));

        var reports = calculator.Compute(Programs(), Previews(pvw1: ["cam1"]), [mapping]);

        var report = Assert.Single(reports);
        Assert.Equal(Preview, report.Colors[0]); // Pgm1Src1
    }

    [Fact]
    public void Compute_Src2BoundToSameSourceAsSrc1_TracksIndependently()
    {
        var calculator = new BacklightCalculator();
        var mapping = new ModuleMapping(0, new ModuleSourceBinding("cam1", "transition"), new ModuleSourceBinding("cam1", "opacity"));

        var reports = calculator.Compute(Programs(pgm1: ["cam1"]), Previews(), [mapping]);

        var report = Assert.Single(reports);
        Assert.Equal(Program, report.Colors[0]); // Pgm1Src1
        Assert.Equal(Program, report.Colors[1]); // Pgm1Src2 (same source id, also on PGM1)
    }

    [Fact]
    public void Compute_Pgm2AndPvw2AreIndependentOfBus1()
    {
        var calculator = new BacklightCalculator();
        var mapping = new ModuleMapping(0, new ModuleSourceBinding("cam1", "transition"), new ModuleSourceBinding("cam2", "opacity"));

        var reports = calculator.Compute(
            Programs(pgm2: ["cam1"]),
            Previews(pvw2: ["cam2"]),
            [mapping]);

        var report = Assert.Single(reports);
        Assert.Equal(Selectable, report.Colors[0]); // Pgm1Src1: bound but not active on bus1
        Assert.Equal(Selectable, report.Colors[1]); // Pgm1Src2: bound but not active on bus1
        Assert.Equal(Program, report.Colors[2]); // Pgm2Src1: cam1 active on PGM2
        Assert.Equal(Preview, report.Colors[3]); // Pgm2Src2: cam2 on PVW2
    }

    [Fact]
    public void Compute_MultipleModules_ProducesOneReportPerModuleWithMatchingIndex()
    {
        var calculator = new BacklightCalculator();
        var mappings = new[]
        {
            new ModuleMapping(0, new ModuleSourceBinding(null, "transition"), new ModuleSourceBinding(null, "opacity")),
            new ModuleMapping(5, new ModuleSourceBinding(null, "transition"), new ModuleSourceBinding(null, "opacity")),
        };

        var reports = calculator.Compute(Programs(), Previews(), mappings);

        Assert.Equal(2, reports.Count);
        Assert.Equal(0, reports[0].ModuleIndex);
        Assert.Equal(5, reports[1].ModuleIndex);
        Assert.All(reports, r => Assert.Equal(4, r.Colors.Count));
    }

    [Fact]
    public void Compute_CustomPolicy_IsUsedInsteadOfDefault()
    {
        var calculator = new BacklightCalculator(new AllBlueBacklightPolicy());
        var mapping = new ModuleMapping(0, new ModuleSourceBinding(null, "transition"), new ModuleSourceBinding(null, "opacity"));

        var reports = calculator.Compute(Programs(), Previews(), [mapping]);

        var report = Assert.Single(reports);
        Assert.All(report.Colors, c => Assert.Equal(new BacklightColor(0, 0, 255), c));
    }

    private sealed class AllBlueBacklightPolicy : IBacklightPolicy
    {
        public BacklightColor Compute(BacklightSwitchContext context) => new(0, 0, 255);
    }
}
