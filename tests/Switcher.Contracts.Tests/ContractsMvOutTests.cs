using System.Text.Json;
using Switcher.Contracts;

namespace Switcher.Contracts.Tests;

/// <summary>
/// Round-trip / backward-compat coverage for the multiview-output revision contract additions
/// (docs/specs/multiview-output-revision.md §4.1): NDI output sinks, region-based multiview,
/// device enumeration and SRT setup DTOs.
/// </summary>
public class ContractsMvOutTests
{
    private static readonly JsonSerializerOptions Options = ProtocolJsonOptions.Default;

    [Fact]
    public void OutputSink_NdiMembers_SerializeWithSpecNames()
    {
        Assert.Equal("\"NDI1\"", JsonSerializer.Serialize(OutputSink.Ndi1, Options));
        Assert.Equal("\"NDI2\"", JsonSerializer.Serialize(OutputSink.Ndi2, Options));
        Assert.Equal(OutputSink.Ndi1, JsonSerializer.Deserialize<OutputSink>("\"NDI1\"", Options));
        Assert.Equal(OutputSink.Ndi2, JsonSerializer.Deserialize<OutputSink>("\"NDI2\"", Options));
    }

    [Fact]
    public void OutputAssignment_NdiName_RoundTrips()
    {
        var assignment = new OutputAssignment(
            OutputSink.Ndi1, OutputSource.Pgm1, null, null, null, "Studio (Program)");

        var json = JsonSerializer.Serialize(assignment, Options);
        Assert.Contains("\"sink\":\"NDI1\"", json);
        Assert.Contains("\"ndi_name\":\"Studio (Program)\"", json);

        Assert.Equal(assignment, JsonSerializer.Deserialize<OutputAssignment>(json, Options));
    }

    [Fact]
    public void OutputAssignment_LegacyJson_WithoutNdiName_Deserializes()
    {
        // Payload shaped exactly like the pre-revision contract: no ndi_name member, and the
        // ordinal-less "HDMI" sink token from before the output table became operator-editable.
        const string legacyJson =
            "{\"sink\":\"HDMI\",\"source\":\"PGM1\",\"display_id\":1,\"hide_cursor\":true,\"fullscreen\":true}";

        var assignment = JsonSerializer.Deserialize<OutputAssignment>(legacyJson, Options);

        Assert.NotNull(assignment);
        Assert.Equal(OutputSink.Hdmi1, assignment.Sink);
        Assert.Equal(1, assignment.DisplayId);
        Assert.Null(assignment.NdiName);
    }

    [Fact]
    public void MultiviewLayout_LegacyCells_RoundTrips()
    {
        var layout = new MultiviewLayout(new[]
        {
            "PGM1", "PGM2", "PVW1", "PVW2",
            "SRC:src-1", "EMPTY", "EMPTY", "EMPTY",
            "EMPTY", "EMPTY", "EMPTY", "EMPTY",
            "EMPTY", "EMPTY", "EMPTY", "EMPTY",
        });

        var json = JsonSerializer.Serialize(layout, Options);
        Assert.Contains("\"cells\":[", json);

        var roundTripped = JsonSerializer.Deserialize<MultiviewLayout>(json, Options);
        Assert.NotNull(roundTripped);
        Assert.Equal(layout.Cells, roundTripped.Cells);
        Assert.Null(roundTripped.Grid);
        Assert.Null(roundTripped.Regions);
    }

    [Fact]
    public void MultiviewLayout_LegacyPayload_WithoutGridOrRegions_Accepted()
    {
        const string legacyJson = "{\"cells\":[\"PGM1\",\"EMPTY\"]}";

        var layout = JsonSerializer.Deserialize<MultiviewLayout>(legacyJson, Options);

        Assert.NotNull(layout);
        Assert.Equal(new[] { "PGM1", "EMPTY" }, layout.Cells);
        Assert.Null(layout.Grid);
        Assert.Null(layout.Regions);
    }

    [Fact]
    public void MultiviewLayout_Regions_RoundTrips()
    {
        var layout = new MultiviewLayout(
            Cells: null!,
            Grid: new MultiviewGrid(2, 2),
            Regions: new[]
            {
                new MultiviewRegion(0, 0, 2, 1, "PGM1"),
                new MultiviewRegion(0, 1, 1, 1, "PVW1"),
                new MultiviewRegion(1, 1, 1, 1, "SRC:src-1"),
            });

        var json = JsonSerializer.Serialize(layout, Options);
        Assert.Contains("\"grid\":{\"rows\":2,\"cols\":2}", json);
        Assert.Contains("\"row_span\":2", json);
        Assert.Contains("\"col_span\":1", json);
        Assert.Contains("\"content\":\"PGM1\"", json);

        var roundTripped = JsonSerializer.Deserialize<MultiviewLayout>(json, Options);
        Assert.NotNull(roundTripped);
        Assert.Equal(layout.Grid, roundTripped.Grid);
        Assert.Equal(layout.Regions, roundTripped.Regions);
    }

    [Fact]
    public void MultiviewLayout_RegionsPayload_WithoutCells_Accepted()
    {
        const string regionsJson =
            "{\"grid\":{\"rows\":1,\"cols\":2}," +
            "\"regions\":[{\"row\":0,\"col\":0,\"row_span\":1,\"col_span\":1,\"content\":\"PGM1\"}]}";

        var layout = JsonSerializer.Deserialize<MultiviewLayout>(regionsJson, Options);

        Assert.NotNull(layout);
        Assert.Null(layout.Cells);
        Assert.Equal(new MultiviewGrid(1, 2), layout.Grid);
        var region = Assert.Single(layout.Regions!);
        Assert.Equal("PGM1", region.Content);
    }

    [Fact]
    public void ToRegions_LegacyCells_ExpandsToUnitRegions()
    {
        var cells = new[]
        {
            "PGM1", "PGM2", "PVW1", "PVW2",
            "PGM1", "PGM2", "PVW1", "PVW2",
            "PGM1", "PGM2", "PVW1", "PVW2",
            "PGM1", "PGM2", "PVW1", "EMPTY",
        };
        var layout = new MultiviewLayout(cells);

        var regions = MultiviewLayoutNormalizer.ToRegions(layout);

        Assert.Equal(16, regions.Count);
        Assert.All(regions, r =>
        {
            Assert.Equal(1, r.RowSpan);
            Assert.Equal(1, r.ColSpan);
        });
        // index 6 -> row 1, col 2
        Assert.Equal(1, regions[6].Row);
        Assert.Equal(2, regions[6].Col);
        Assert.Equal("PVW1", regions[6].Content);
        // last cell index 15 -> row 3, col 3
        Assert.Equal(3, regions[15].Row);
        Assert.Equal(3, regions[15].Col);
        Assert.Equal("EMPTY", regions[15].Content);
    }

    [Fact]
    public void ToRegions_Regions_ReturnedAsIs()
    {
        var regions = new[]
        {
            new MultiviewRegion(0, 0, 2, 2, "PGM1"),
            new MultiviewRegion(0, 2, 1, 1, "PVW1"),
        };
        var layout = new MultiviewLayout(Cells: null!, Grid: new MultiviewGrid(2, 3), Regions: regions);

        var result = MultiviewLayoutNormalizer.ToRegions(layout);

        Assert.Same(regions, result);
    }

    [Fact]
    public void DeviceInfo_RoundTrips()
    {
        var webcam = new DeviceInfo("dev0", "USB Camera", new[] { "1920x1080@30", "1280x720@60" });
        var webcamJson = JsonSerializer.Serialize(webcam, Options);
        Assert.Contains("\"id\":\"dev0\"", webcamJson);
        Assert.Contains("\"formats\":[\"1920x1080@30\",\"1280x720@60\"]", webcamJson);
        var webcamRt = JsonSerializer.Deserialize<DeviceInfo>(webcamJson, Options);
        Assert.NotNull(webcamRt);
        Assert.Equal(webcam.Id, webcamRt.Id);
        Assert.Equal(webcam.Name, webcamRt.Name);
        Assert.Equal(webcam.Formats, webcamRt.Formats);

        var ndi = new DeviceInfo("STUDIO (Cam1)", "STUDIO (Cam1)", null);
        var ndiJson = JsonSerializer.Serialize(ndi, Options);
        Assert.Contains("\"formats\":null", ndiJson);
        var ndiRt = JsonSerializer.Deserialize<DeviceInfo>(ndiJson, Options);
        Assert.NotNull(ndiRt);
        Assert.Equal(ndi.Id, ndiRt.Id);
        Assert.Null(ndiRt.Formats);
    }

    [Fact]
    public void DeviceQueryType_SerializesWithSpecNames()
    {
        Assert.Equal("\"WEBCAM\"", JsonSerializer.Serialize(DeviceQueryType.Webcam, Options));
        Assert.Equal("\"NDI\"", JsonSerializer.Serialize(DeviceQueryType.Ndi, Options));
    }

    [Fact]
    public void SrtSetupInfo_RoundTrips()
    {
        var info = new SrtSetupInfo(
            ProtocolConstants.SrtListenPort,
            new[] { "192.168.1.50", "10.0.0.12" },
            "srt://192.168.1.50:9000",
            120,
            "Set your encoder to caller mode targeting the URL above.");

        var json = JsonSerializer.Serialize(info, Options);
        Assert.Contains("\"listener_port\":9000", json);
        Assert.Contains("\"host_candidates\":[\"192.168.1.50\",\"10.0.0.12\"]", json);
        Assert.Contains("\"recommended_url\":\"srt://192.168.1.50:9000\"", json);
        Assert.Contains("\"recommended_latency_ms\":120", json);

        var roundTripped = JsonSerializer.Deserialize<SrtSetupInfo>(json, Options);
        Assert.NotNull(roundTripped);
        Assert.Equal(info.ListenerPort, roundTripped.ListenerPort);
        Assert.Equal(info.HostCandidates, roundTripped.HostCandidates);
        Assert.Equal(info.RecommendedUrl, roundTripped.RecommendedUrl);
        Assert.Equal(info.RecommendedLatencyMs, roundTripped.RecommendedLatencyMs);
        Assert.Equal(info.InstructionsText, roundTripped.InstructionsText);
    }
}
