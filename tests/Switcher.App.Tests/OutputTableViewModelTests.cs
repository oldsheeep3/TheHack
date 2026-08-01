using Switcher.App.ViewModels;
using Switcher.Contracts;

namespace Switcher.App.Tests;

/// <summary>
/// The Outputs dock's editable routing table (docs/specs/00-system-overview.md §4.2). The operator
/// starts on one webcam and one display and adds sinks from there, so the interesting behaviour is all
/// at the edges: what a persisted table seeds, what happens at the per-kind and total ceilings, and what
/// a removed sink frees up. These run against the same model the WPF window drives.
/// </summary>
public sealed class OutputTableViewModelTests
{
    [Fact]
    public void Load_WithNothingPersisted_SeedsTheOneWebcamOneDisplayDefault()
    {
        var table = new OutputTableViewModel();

        table.Load([]);

        Assert.Equal(
            [OutputSink.Vcam1, OutputSink.Hdmi1],
            table.Rows.Select(r => r.Sink).ToList());
        Assert.Equal(OutputSource.Pgm1, table.Rows[0].Source);
        Assert.Equal(OutputSource.Pgm2, table.Rows[1].Source);
    }

    [Fact]
    public void Load_SeedsOneRowPerPersistedAssignment_GroupedByKindThenOrdinal()
    {
        var table = new OutputTableViewModel();

        // Deliberately out of order, the way a hand-edited or incrementally grown config looks.
        table.Load(
        [
            new OutputAssignment(OutputSink.Ndi2, OutputSource.Pgm2, null, null, null, NdiName: "STUDIO B"),
            new OutputAssignment(OutputSink.Hdmi3, OutputSource.Pgm2, DisplayId: 2, HideCursor: false, Fullscreen: true),
            new OutputAssignment(OutputSink.Vcam2, OutputSource.Pgm1, null, null, null),
        ]);

        Assert.Equal(
            [OutputSink.Vcam2, OutputSink.Hdmi3, OutputSink.Ndi2],
            table.Rows.Select(r => r.Sink).ToList());

        // The row header has to spell the ordinal out - the rows are no longer in a known fixed order.
        Assert.Equal(["Webcam 2", "HDMI 3", "NDI 2"], table.Rows.Select(r => r.Label).ToList());

        var hdmi = table.Rows[1];
        Assert.True(hdmi.IsHdmi);
        Assert.False(hdmi.IsNdi);
        Assert.Equal(2, hdmi.DisplayId);
        Assert.False(hdmi.HideCursor);

        Assert.True(table.Rows[2].IsNdi);
        Assert.Equal("STUDIO B", table.Rows[2].NdiName);
    }

    [Fact]
    public void Load_ReplacesTheRowsRatherThanAppendingToThem()
    {
        var table = new OutputTableViewModel();

        table.Load([]);
        table.Load([new OutputAssignment(OutputSink.Ndi1, OutputSource.Pgm1, null, null, null)]);

        Assert.Equal([OutputSink.Ndi1], table.Rows.Select(r => r.Sink).ToList());
    }

    [Fact]
    public void Add_FillsTheLowestFreeOrdinalOfTheChosenKind()
    {
        var table = new OutputTableViewModel();
        table.Load([]);  // VCAM1 + HDMI1

        var added = table.Add(OutputKind.Hdmi);

        Assert.NotNull(added);
        Assert.Equal(OutputSink.Hdmi2, added!.Sink);

        // A new HDMI sink is usable straight away: it targets the primary display full-screen.
        Assert.Equal(OutputDefaults.DefaultHdmiDisplayId, added.DisplayId);
        Assert.True(added.Fullscreen);
    }

    [Fact]
    public void Add_NamesANewNdiSinkSoReceiversSeeSomething()
    {
        var table = new OutputTableViewModel();
        table.Load([]);

        var added = table.Add(OutputKind.Ndi);

        Assert.Equal(OutputSink.Ndi1, added!.Sink);
        Assert.Equal(OutputDefaults.NdiSenderName(OutputSink.Ndi1), added.NdiName);
    }

    [Fact]
    public void Add_RefusesAFourthSinkOfTheSameKind()
    {
        var table = new OutputTableViewModel();
        table.Load([new OutputAssignment(OutputSink.Ndi1, OutputSource.Pgm1, null, null, null)]);

        Assert.NotNull(table.Add(OutputKind.Ndi));  // NDI2
        Assert.NotNull(table.Add(OutputKind.Ndi));  // NDI3
        Assert.Null(table.Add(OutputKind.Ndi));

        Assert.Equal(OutputCatalog.MaxSinksPerKind, table.Rows.Count);
        Assert.Equal(
            [OutputSink.Ndi1, OutputSink.Ndi2, OutputSink.Ndi3],
            table.Rows.Select(r => r.Sink).ToList());

        // Another kind is still addable - the ceiling that was hit is the per-kind one.
        Assert.NotNull(table.Add(OutputKind.Hdmi));
    }

    [Fact]
    public void Add_RefusesASecondWebcamWhileTheTableIsStillNearlyEmpty()
    {
        var table = new OutputTableViewModel();
        table.Load([new OutputAssignment(OutputSink.Vcam1, OutputSource.Pgm1, null, null, null)]);

        // OBS exposes one virtual-camera output, so the webcam ceiling is one and is hit four sinks
        // short of the total one - the refusal is about the kind, not about a full table.
        Assert.Null(table.Add(OutputKind.Webcam));
        Assert.Equal([OutputSink.Vcam1], table.Sinks);

        table.SelectedKind = table.KindOptions.First(k => k.Kind == OutputKind.Webcam);
        Assert.False(table.CanAdd);
        Assert.Contains($"{OutputCatalog.MaxWebcamSinks} Webcam", table.AddRefusalReason);

        // ...and the sentence is the singular one: "1 Webcam outputs" would read as a bug in the message.
        Assert.DoesNotContain("outputs", table.AddRefusalReason!, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(OutputKind.Hdmi)]
    [InlineData(OutputKind.Ndi)]
    public void Add_StillTakesThreeOfEveryKindTheVirtualCameraLimitDoesNotTouch(OutputKind kind)
    {
        var table = new OutputTableViewModel();
        table.Load([new OutputAssignment(OutputSink.Vcam1, OutputSource.Pgm1, null, null, null)]);

        Assert.NotNull(table.Add(kind));
        Assert.NotNull(table.Add(kind));
        Assert.NotNull(table.Add(kind));
        Assert.Null(table.Add(kind));

        Assert.Equal(
            OutputCatalog.MaxSinksPerKind,
            table.Sinks.Count(s => OutputCatalog.KindOf(s) == kind));
    }

    [Fact]
    public void Add_RefusesASeventhSinkOverall()
    {
        var table = new OutputTableViewModel();
        table.Load([]);

        // 2 (default) + 2 HDMI + 2 NDI = 6, the total ceiling, with an NDI ordinal still free.
        Assert.NotNull(table.Add(OutputKind.Hdmi));
        Assert.NotNull(table.Add(OutputKind.Hdmi));
        Assert.NotNull(table.Add(OutputKind.Ndi));
        Assert.NotNull(table.Add(OutputKind.Ndi));
        Assert.Equal(OutputCatalog.MaxTotal, table.Rows.Count);

        Assert.Null(table.Add(OutputKind.Ndi));
        Assert.Equal(OutputCatalog.MaxTotal, table.Rows.Count);

        table.SelectedKind = table.KindOptions.First(k => k.Kind == OutputKind.Ndi);
        Assert.False(table.CanAdd);
        Assert.Contains($"{OutputCatalog.MaxTotal} sinks", table.AddRefusalReason);
    }

    [Fact]
    public void Remove_DropsTheRowAndFreesItsOrdinalForReuse()
    {
        var table = new OutputTableViewModel();
        table.Load([]);
        table.Add(OutputKind.Ndi);   // NDI1
        table.Add(OutputKind.Ndi);   // NDI2

        var ndi1 = table.Rows.Single(r => r.Sink == OutputSink.Ndi1);
        Assert.True(table.Remove(ndi1));
        Assert.DoesNotContain(OutputSink.Ndi1, table.Sinks);

        // Adding an NDI back reuses the freed ordinal rather than climbing to NDI3.
        Assert.Equal(OutputSink.Ndi1, table.Add(OutputKind.Ndi)!.Sink);
    }

    [Fact]
    public void CanAdd_AndSummary_FollowTheTableAsItGrows()
    {
        var table = new OutputTableViewModel();
        table.Load([]);
        table.SelectedKind = table.KindOptions.First(k => k.Kind == OutputKind.Hdmi);

        Assert.True(table.CanAdd);
        Assert.Null(table.AddRefusalReason);
        Assert.Equal($"2/{OutputCatalog.MaxTotal} SINKS", table.Summary);

        table.Add(OutputKind.Hdmi);
        table.Add(OutputKind.Hdmi);

        Assert.False(table.CanAdd);
        Assert.Contains($"{OutputCatalog.MaxOf(OutputKind.Hdmi)} HDMI", table.AddRefusalReason);
        Assert.Equal($"4/{OutputCatalog.MaxTotal} SINKS", table.Summary);
    }

    [Fact]
    public void SelectedKind_IgnoresTheBlankSelectionWpfPushesWhileRebindingACombo()
    {
        var table = new OutputTableViewModel();
        var hdmi = table.KindOptions.First(k => k.Kind == OutputKind.Hdmi);

        table.SelectedKind = hdmi;
        table.SelectedKind = null!;

        Assert.Equal(hdmi, table.SelectedKind);
    }

    [Fact]
    public void Validate_AcceptsATableWhereEveryBusHasSomewhereToGo()
    {
        var table = new OutputTableViewModel();
        table.Load([]);

        Assert.Empty(table.Validate());
    }

    [Fact]
    public void Validate_RejectsATableThatLeavesAProgramBusWithNoOutput()
    {
        var table = new OutputTableViewModel();
        table.Load([new OutputAssignment(OutputSink.Vcam1, OutputSource.Pgm1, null, null, null)]);

        var error = Assert.Single(table.Validate());

        Assert.Contains("Pgm2", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_RejectsAHandEditedTableThatIsOverTheCeilings()
    {
        // The + button cannot build this; only a config edited outside the app can.
        var table = new OutputTableViewModel();
        table.Load(
        [
            .. OutputCatalog.Sinks(OutputKind.Webcam)
                .Select(s => new OutputAssignment(s, OutputSource.Pgm1, null, null, null)),
            .. OutputCatalog.Sinks(OutputKind.Hdmi)
                .Select(s => new OutputAssignment(s, OutputSource.Pgm2, null, null, null)),
            new OutputAssignment(OutputSink.Ndi1, OutputSource.Pgm1, null, null, null),
        ]);

        // Loaded as-is so the operator can see and fix it, but not applicable.
        Assert.Equal(7, table.Rows.Count);
        Assert.Contains(table.Validate(), e => e.Contains($"at most {OutputCatalog.MaxTotal}", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_RejectsTheTwoWebcamDefaultPersistedByBuildsPredatingTheOneCameraCeiling()
    {
        // What a build from before OutputCatalog.MaxWebcamSinks saved: PGM1→VCAM1, PGM2→VCAM2. VCAM2 is
        // still a sink the catalog knows, so the config deserializes and loads - it is the ceiling, not
        // the parser, that refuses it, which is what lets the operator see what has to change.
        var table = new OutputTableViewModel();
        table.Load(
        [
            new OutputAssignment(OutputSink.Vcam1, OutputSource.Pgm1, null, null, null),
            new OutputAssignment(OutputSink.Vcam2, OutputSource.Pgm2, null, null, null),
        ]);

        Assert.Equal([OutputSink.Vcam1, OutputSink.Vcam2], table.Sinks);

        // Both buses have somewhere to go, so the only complaint is the webcam count.
        var error = Assert.Single(table.Validate());
        Assert.Equal(
            $"outputs holds 2 WEBCAM sinks; at most {OutputCatalog.MaxWebcamSinks} are allowed.", error);
    }

    [Fact]
    public void ToRequest_CarriesOnlyTheFieldsEachKindOwns()
    {
        var table = new OutputTableViewModel();
        table.Load([]);
        table.Add(OutputKind.Ndi);

        var outputs = table.ToRequest().Outputs;

        var webcam = outputs.Single(o => o.Sink == OutputSink.Vcam1);
        Assert.Null(webcam.DisplayId);
        Assert.Null(webcam.NdiName);

        var hdmi = outputs.Single(o => o.Sink == OutputSink.Hdmi1);
        Assert.Equal(OutputDefaults.DefaultHdmiDisplayId, hdmi.DisplayId);
        Assert.Null(hdmi.NdiName);

        var ndi = outputs.Single(o => o.Sink == OutputSink.Ndi1);
        Assert.Null(ndi.DisplayId);
        Assert.Equal(OutputDefaults.NdiSenderName(OutputSink.Ndi1), ndi.NdiName);
    }
}
