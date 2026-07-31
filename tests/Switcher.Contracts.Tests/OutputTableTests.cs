using System.Text.Json;
using Switcher.Contracts;

namespace Switcher.Contracts.Tests;

/// <summary>
/// The output table is operator-editable (docs/specs/00-system-overview.md §4.2): it starts at one
/// webcam plus one display and grows to at most <see cref="OutputCatalog.MaxOf"/> sinks of a kind — one
/// webcam, three of anything else — and <see cref="OutputCatalog.MaxTotal"/> altogether. Two things have
/// to hold for an upgrade not to hurt: the tokens sitting in existing configs still parse, including the
/// <c>VCAM2</c> no operator can add any more, and every sink writes back in the canonical form the native
/// engine switches on.
/// </summary>
public class OutputTableTests
{
    private static readonly JsonSerializerOptions Options = ProtocolJsonOptions.Default;

    [Theory]
    [InlineData(OutputSink.Vcam1, "VCAM1")]
    [InlineData(OutputSink.Vcam2, "VCAM2")]
    [InlineData(OutputSink.Vcam3, "VCAM3")]
    [InlineData(OutputSink.Hdmi1, "HDMI1")]
    [InlineData(OutputSink.Hdmi2, "HDMI2")]
    [InlineData(OutputSink.Hdmi3, "HDMI3")]
    [InlineData(OutputSink.Ndi1, "NDI1")]
    [InlineData(OutputSink.Ndi2, "NDI2")]
    [InlineData(OutputSink.Ndi3, "NDI3")]
    public void OutputSink_RoundTripsAsItsCanonicalToken(OutputSink sink, string token)
    {
        Assert.Equal($"\"{token}\"", JsonSerializer.Serialize(sink, Options));
        Assert.Equal(sink, JsonSerializer.Deserialize<OutputSink>($"\"{token}\"", Options));
        Assert.Equal(token, OutputCatalog.TokenOf(sink));
    }

    [Theory]
    [InlineData("HDMI", OutputSink.Hdmi1, "HDMI1")]
    [InlineData("VCAM", OutputSink.Vcam1, "VCAM1")]
    [InlineData("WEBCAM", OutputSink.Vcam1, "VCAM1")]
    [InlineData("NDI", OutputSink.Ndi1, "NDI1")]
    public void OutputSink_LegacyOrdinalLessToken_ReadsAsFirstSinkAndRewritesCanonically(
        string legacyToken, OutputSink expected, string canonicalToken)
    {
        var sink = JsonSerializer.Deserialize<OutputSink>($"\"{legacyToken}\"", Options);

        Assert.Equal(expected, sink);
        Assert.Equal($"\"{canonicalToken}\"", JsonSerializer.Serialize(sink, Options));
    }

    [Fact]
    public void OutputSink_UnknownToken_Throws()
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<OutputSink>("\"HDMI4\"", Options));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<OutputSink>("\"SDI1\"", Options));
    }

    [Fact]
    public void MaxOf_IsOneWebcamAndThreeOfEveryOtherKind()
    {
        Assert.Equal(1, OutputCatalog.MaxOf(OutputKind.Webcam));
        Assert.Equal(3, OutputCatalog.MaxOf(OutputKind.Hdmi));
        Assert.Equal(3, OutputCatalog.MaxOf(OutputKind.Ndi));
    }

    [Fact]
    public void NextAvailable_FillsTheLowestFreeOrdinal()
    {
        Assert.Equal(OutputSink.Vcam1, OutputCatalog.NextAvailable([], OutputKind.Webcam));
        Assert.Equal(OutputSink.Hdmi2, OutputCatalog.NextAvailable([OutputSink.Hdmi1], OutputKind.Hdmi));
        // Removing NDI1 and adding an NDI back reuses NDI1 rather than climbing to NDI3.
        Assert.Equal(OutputSink.Ndi1, OutputCatalog.NextAvailable([OutputSink.Ndi2], OutputKind.Ndi));
        Assert.Equal(OutputSink.Hdmi2, OutputCatalog.NextAvailable([OutputSink.Hdmi1, OutputSink.Hdmi3], OutputKind.Hdmi));
    }

    [Fact]
    public void NextAvailable_SecondWebcam_IsNullEvenThoughVcam2HasAToken()
    {
        // VCAM2 still exists as a token so old configs deserialize, but OBS has one virtual camera to
        // give, so the operator is never offered a second webcam sink.
        OutputSink[] oneWebcam = [OutputSink.Vcam1];

        Assert.Null(OutputCatalog.NextAvailable(oneWebcam, OutputKind.Webcam));
        Assert.False(OutputCatalog.CanAdd(oneWebcam, OutputKind.Webcam));
        Assert.Equal(OutputSink.Hdmi1, OutputCatalog.NextAvailable(oneWebcam, OutputKind.Hdmi));
    }

    [Fact]
    public void NextAvailable_KindFull_IsNullButOtherKindsStillHaveRoom()
    {
        OutputSink[] displays = [OutputSink.Hdmi1, OutputSink.Hdmi2, OutputSink.Hdmi3];

        Assert.Null(OutputCatalog.NextAvailable(displays, OutputKind.Hdmi));
        Assert.False(OutputCatalog.CanAdd(displays, OutputKind.Hdmi));
        Assert.Equal(OutputSink.Ndi1, OutputCatalog.NextAvailable(displays, OutputKind.Ndi));
    }

    [Fact]
    public void NextAvailable_TableFull_IsNullEvenWhenTheKindHasRoom()
    {
        // Six sinks: one webcam, three displays, two NDI senders. NDI is still a sink short of MaxOf,
        // and the table is at MaxTotal.
        OutputSink[] full =
        [
            OutputSink.Vcam1,
            OutputSink.Hdmi1, OutputSink.Hdmi2, OutputSink.Hdmi3,
            OutputSink.Ndi1, OutputSink.Ndi2,
        ];
        Assert.Equal(OutputCatalog.MaxTotal, full.Length);

        foreach (var kind in Enum.GetValues<OutputKind>())
        {
            Assert.Null(OutputCatalog.NextAvailable(full, kind));
            Assert.False(OutputCatalog.CanAdd(full, kind));
        }
    }

    [Fact]
    public void DescribeOverLimit_FourthSinkOfAKind_IsRejected()
    {
        var outputs = new[]
        {
            OutputDefaults.NewAssignment(OutputSink.Hdmi1, OutputSource.Pgm1),
            OutputDefaults.NewAssignment(OutputSink.Hdmi2, OutputSource.Pgm1),
            OutputDefaults.NewAssignment(OutputSink.Hdmi3, OutputSource.Pgm2),
            // A fourth display sink cannot exist as an ordinal, but a hand-edited config can repeat one.
            OutputDefaults.NewAssignment(OutputSink.Hdmi3, OutputSource.Pgm2),
        };

        var error = Assert.Single(OutputRules.DescribeOverLimit(outputs));
        Assert.Contains("HDMI", error);
        Assert.Contains("4", error);
    }

    [Fact]
    public void DescribeOverLimit_SecondWebcam_IsRejected()
    {
        var outputs = new[]
        {
            OutputDefaults.NewAssignment(OutputSink.Vcam1, OutputSource.Pgm1),
            OutputDefaults.NewAssignment(OutputSink.Vcam2, OutputSource.Pgm2),
        };

        var error = Assert.Single(OutputRules.DescribeOverLimit(outputs));
        Assert.Equal("outputs holds 2 WEBCAM sinks; at most 1 are allowed.", error);
    }

    [Fact]
    public void DescribeOverLimit_SeventhSinkOverall_IsRejected()
    {
        var outputs = new[]
        {
            OutputDefaults.NewAssignment(OutputSink.Vcam1, OutputSource.Pgm1),
            OutputDefaults.NewAssignment(OutputSink.Hdmi1, OutputSource.Pgm2),
            OutputDefaults.NewAssignment(OutputSink.Hdmi2, OutputSource.Pgm2),
            OutputDefaults.NewAssignment(OutputSink.Hdmi3, OutputSource.Pgm2),
            OutputDefaults.NewAssignment(OutputSink.Ndi1, OutputSource.Pgm1),
            OutputDefaults.NewAssignment(OutputSink.Ndi2, OutputSource.Pgm1),
            OutputDefaults.NewAssignment(OutputSink.Ndi3, OutputSource.Pgm2),
        };

        var error = Assert.Single(OutputRules.DescribeOverLimit(outputs));
        Assert.Contains("7", error);
        Assert.Contains(OutputCatalog.MaxTotal.ToString(), error);
    }

    [Fact]
    public void DescribeOverLimit_OneWebcamAndFiveOthersAtSixTotal_IsSilent()
    {
        var outputs = new[]
        {
            OutputDefaults.NewAssignment(OutputSink.Vcam1, OutputSource.Pgm1),
            OutputDefaults.NewAssignment(OutputSink.Hdmi1, OutputSource.Pgm2),
            OutputDefaults.NewAssignment(OutputSink.Hdmi2, OutputSource.Pgm2),
            OutputDefaults.NewAssignment(OutputSink.Hdmi3, OutputSource.Pgm2),
            OutputDefaults.NewAssignment(OutputSink.Ndi1, OutputSource.Pgm1),
            OutputDefaults.NewAssignment(OutputSink.Ndi2, OutputSource.Pgm1),
        };

        Assert.Empty(OutputRules.DescribeOverLimit(outputs));
        Assert.Empty(OutputRules.DescribeOverLimit(null));
    }

    [Fact]
    public void LegacyTwoWebcamTable_StillDeserializes_ButIsCaughtAsOverLimit()
    {
        // What a build that predated the one-webcam limit wrote as its default. It has to load — the App
        // cannot start by choking on the config it wrote itself last week — and it has to be caught, so
        // the App can fall back to the current defaults instead of running a table it can no longer offer.
        const string legacyJson =
            """
            {"outputs":[{"sink":"VCAM1","source":"PGM1"},{"sink":"VCAM2","source":"PGM2"}]}
            """;

        var request = JsonSerializer.Deserialize<OutputsRequest>(legacyJson, Options);

        Assert.NotNull(request);
        Assert.Equal(
            new[] { OutputSink.Vcam1, OutputSink.Vcam2 },
            request.Outputs.Select(o => o.Sink));
        Assert.Empty(OutputRules.MissingBuses(request.Outputs));

        var error = Assert.Single(OutputRules.DescribeOverLimit(request.Outputs));
        Assert.Equal("outputs holds 2 WEBCAM sinks; at most 1 are allowed.", error);
    }

    [Fact]
    public void Default_IsOneWebcamAndOneDisplayCoveringBothBuses()
    {
        // The reason the default is VCAM1 + HDMI1 rather than two webcams: every program bus needs an
        // output, and the smallest table that satisfies that is one sink per bus.
        Assert.Equal(
            new[] { OutputSink.Vcam1, OutputSink.Hdmi1 },
            OutputDefaults.Default.Select(o => o.Sink));
        Assert.Empty(OutputRules.MissingBuses(OutputDefaults.Default));
        Assert.Empty(OutputRules.DescribeOverLimit(OutputDefaults.Default));
    }

    [Fact]
    public void DefaultWithNdi_AddsBothNdiSendersAndStaysWithinTheLimits()
    {
        Assert.Equal(
            new[] { OutputSink.Vcam1, OutputSink.Hdmi1, OutputSink.Ndi1, OutputSink.Ndi2 },
            OutputDefaults.DefaultWithNdi.Select(o => o.Sink));
        Assert.Empty(OutputRules.MissingBuses(OutputDefaults.DefaultWithNdi));
        Assert.Empty(OutputRules.DescribeOverLimit(OutputDefaults.DefaultWithNdi));
    }

    [Fact]
    public void OutputsRequest_WithAddedSinks_RoundTripsEveryToken()
    {
        var request = new OutputsRequest(new[]
        {
            OutputDefaults.NewAssignment(OutputSink.Vcam1, OutputSource.Pgm1),
            OutputDefaults.NewAssignment(OutputSink.Hdmi2, OutputSource.Pgm1),
            OutputDefaults.NewAssignment(OutputSink.Hdmi3, OutputSource.Pgm2),
            OutputDefaults.NewAssignment(OutputSink.Ndi3, OutputSource.Pgm2),
        });

        var json = JsonSerializer.Serialize(request, Options);
        Assert.Contains("\"sink\":\"VCAM1\"", json);
        Assert.Contains("\"sink\":\"HDMI2\"", json);
        Assert.Contains("\"sink\":\"NDI3\"", json);
        Assert.Contains($"\"ndi_name\":\"{OutputDefaults.Ndi3SenderName}\"", json);

        var roundTripped = JsonSerializer.Deserialize<OutputsRequest>(json, Options);
        Assert.NotNull(roundTripped);
        Assert.Equal(request.Outputs, roundTripped.Outputs);
    }

    [Fact]
    public void OutputKind_SerializesWithSpecNames()
    {
        Assert.Equal("\"WEBCAM\"", JsonSerializer.Serialize(OutputKind.Webcam, Options));
        Assert.Equal("\"HDMI\"", JsonSerializer.Serialize(OutputKind.Hdmi, Options));
        Assert.Equal("\"NDI\"", JsonSerializer.Serialize(OutputKind.Ndi, Options));
    }
}
