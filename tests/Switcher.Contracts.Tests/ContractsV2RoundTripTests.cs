using System.Text.Json;
using Switcher.Contracts;

namespace Switcher.Contracts.Tests;

public class ContractsV2RoundTripTests
{
    private static readonly JsonSerializerOptions Options = ProtocolJsonOptions.Default;

    [Fact]
    public void SourceDefinition_Ndi_SerializesWithSpecFieldNames()
    {
        var source = new SourceDefinition(
            "src-ndi-cam1",
            "Cam 1 (NDI)",
            SourceType.Ndi,
            new NdiConfig("STUDIO (Cam1)"),
            null,
            null);

        var json = JsonSerializer.Serialize(source, Options);

        Assert.Contains("\"type\":\"NDI\"", json);
        Assert.Contains("\"ndi\":{\"source_name\":\"STUDIO (Cam1)\"}", json);
        Assert.Contains("\"webcam\":null", json);
        Assert.Contains("\"srt\":null", json);

        var roundTripped = JsonSerializer.Deserialize<SourceDefinition>(json, Options);
        Assert.Equal(source, roundTripped);
    }

    [Fact]
    public void SourceDefinition_Webcam_And_Srt_RoundTrip()
    {
        var webcam = new SourceDefinition(
            "src-webcam-1", "Webcam", SourceType.Webcam, null, new WebcamConfig("dev0", "1920x1080@30"), null);
        var webcamJson = JsonSerializer.Serialize(webcam, Options);
        Assert.Contains("\"type\":\"WEBCAM\"", webcamJson);
        Assert.Contains("\"device_id\":\"dev0\"", webcamJson);
        Assert.Equal(webcam, JsonSerializer.Deserialize<SourceDefinition>(webcamJson, Options));

        var srt = new SourceDefinition(
            "src-srt-1", "SRT In", SourceType.Srt, null, null,
            new SrtConfig("srt://192.168.1.100:9000?mode=caller", 40));
        var srtJson = JsonSerializer.Serialize(srt, Options);
        Assert.Contains("\"type\":\"SRT\"", srtJson);
        Assert.Contains("\"latency_ms\":40", srtJson);
        Assert.Equal(srt, JsonSerializer.Deserialize<SourceDefinition>(srtJson, Options));
    }

    [Fact]
    public void SourceInfo_WithIdAndOrder_RoundTrips()
    {
        var source = new SourceInfo(1, "cam-1", SourceProtocol.Uvc, "1920x1080", SourceStatus.Connected, "src-1", 0);

        var json = JsonSerializer.Serialize(source, Options);
        Assert.Contains("\"id\":\"src-1\"", json);
        Assert.Contains("\"order\":0", json);

        var roundTripped = JsonSerializer.Deserialize<SourceInfo>(json, Options);
        Assert.Equal(source, roundTripped);
    }

    [Fact]
    public void ProgramRequest_SerializesWithSpecFieldNames()
    {
        var request = new ProgramRequest(
            ProgramBus.Pgm1,
            new[]
            {
                new ProgramLayer("src-ndi-cam1", new PipSettings(true, 0, 0, 1920, 1080, 1.0, 0, null)),
            },
            false);

        var json = JsonSerializer.Serialize(request, Options);

        Assert.Contains("\"bus\":\"PGM1\"", json);
        Assert.Contains("\"source_id\":\"src-ndi-cam1\"", json);
        Assert.Contains("\"x_position\":0", json);
        Assert.Contains("\"take\":false", json);

        var roundTripped = JsonSerializer.Deserialize<ProgramRequest>(json, Options);
        Assert.NotNull(roundTripped);
        Assert.Equal(request.Bus, roundTripped.Bus);
        Assert.Equal(request.Take, roundTripped.Take);
        Assert.Equal(request.Layers, roundTripped.Layers);
    }

    [Fact]
    public void MultiviewLayout_RoundTrips()
    {
        var layout = new MultiviewLayout(new[]
        {
            "PGM1", "PGM2", "PVW1", "PVW2",
            "SRC:src-ndi-cam1", "SRC:src-webcam-1", "EMPTY", "EMPTY",
            "EMPTY", "EMPTY", "EMPTY", "EMPTY",
            "EMPTY", "EMPTY", "EMPTY", "EMPTY",
        });

        var json = JsonSerializer.Serialize(layout, Options);
        Assert.Contains("\"cells\":[", json);

        var roundTripped = JsonSerializer.Deserialize<MultiviewLayout>(json, Options);
        Assert.NotNull(roundTripped);
        Assert.Equal(layout.Cells, roundTripped.Cells);
    }

    [Fact]
    public void OutputsRequest_SerializesWithSpecFieldNames()
    {
        var request = new OutputsRequest(new[]
        {
            new OutputAssignment(OutputSink.Vcam1, OutputSource.Pgm1, null, null, null),
            new OutputAssignment(OutputSink.Hdmi, OutputSource.Pgm1, 1, true, true),
        });

        var json = JsonSerializer.Serialize(request, Options);

        Assert.Contains("\"sink\":\"VCAM1\"", json);
        Assert.Contains("\"sink\":\"HDMI\"", json);
        Assert.Contains("\"display_id\":1", json);
        Assert.Contains("\"hide_cursor\":true", json);
        Assert.Contains("\"fullscreen\":true", json);

        var roundTripped = JsonSerializer.Deserialize<OutputsRequest>(json, Options);
        Assert.NotNull(roundTripped);
        Assert.Equal(request.Outputs, roundTripped.Outputs);
    }

    [Fact]
    public void AtemConfig_SerializesWithSpecFieldNames()
    {
        var config = new AtemConfig(
            true,
            "192.168.1.240",
            new[]
            {
                new AtemButtonMapping("main", 0, "PGM1xSRC1", "ProgramInput", 0, 1),
            });

        var json = JsonSerializer.Serialize(config, Options);

        Assert.Contains("\"controller_id\":\"main\"", json);
        Assert.Contains("\"module_index\":0", json);
        Assert.Contains("\"mix_effect\":0", json);

        var roundTripped = JsonSerializer.Deserialize<AtemConfig>(json, Options);
        Assert.NotNull(roundTripped);
        Assert.Equal(config.Enabled, roundTripped.Enabled);
        Assert.Equal(config.Ip, roundTripped.Ip);
        Assert.Equal(config.Mappings, roundTripped.Mappings);
    }

    [Fact]
    public void AtemCommandRequest_RoundTrips()
    {
        var request = new AtemCommandRequest("Cut", 0, 2);

        var json = JsonSerializer.Serialize(request, Options);
        var roundTripped = JsonSerializer.Deserialize<AtemCommandRequest>(json, Options);

        Assert.Equal(request, roundTripped);
    }

    [Fact]
    public void ModulesRequest_SerializesWithSpecFieldNames()
    {
        var request = new ModulesRequest(new[]
        {
            new ModuleMapping(
                0,
                new ModuleSourceBinding("src-ndi-cam1", "transition"),
                new ModuleSourceBinding("src-webcam-1", "opacity")),
        });

        var json = JsonSerializer.Serialize(request, Options);

        Assert.Contains("\"vr_target\":\"transition\"", json);
        Assert.Contains("\"vr_target\":\"opacity\"", json);
        Assert.Contains("\"index\":0", json);

        var roundTripped = JsonSerializer.Deserialize<ModulesRequest>(json, Options);
        Assert.NotNull(roundTripped);
        Assert.Equal(request.Modules, roundTripped.Modules);
    }

    [Fact]
    public void TallyStateV2_SerializesWithSpecFieldNames()
    {
        var state = new TallyStateV2(new[] { 1, 3 }, new[] { 2 }, new[] { 4 }, Array.Empty<int>());

        var json = JsonSerializer.Serialize(state, Options);

        Assert.Contains("\"active_pgm1\":[1,3]", json);
        Assert.Contains("\"active_pgm2\":[2]", json);
        Assert.Contains("\"active_pvw1\":[4]", json);
        Assert.Contains("\"active_pvw2\":[]", json);

        var roundTripped = JsonSerializer.Deserialize<TallyStateV2>(json, Options);
        Assert.Equal(state.ActivePgm1, roundTripped?.ActivePgm1);
        Assert.Equal(state.ActivePgm2, roundTripped?.ActivePgm2);
        Assert.Equal(state.ActivePvw1, roundTripped?.ActivePvw1);
        Assert.Equal(state.ActivePvw2, roundTripped?.ActivePvw2);
    }

    [Fact]
    public void PicoNetworkConfig_SerializesWithSpecFieldNames()
    {
        var config = new PicoNetworkConfig("studio-wifi", "hunter2", "main", true);

        var json = JsonSerializer.Serialize(config, Options);

        Assert.Contains("\"wifi_ssid\":\"studio-wifi\"", json);
        Assert.Contains("\"wifi_password\":\"hunter2\"", json);
        Assert.Contains("\"controller_id\":\"main\"", json);
        Assert.Contains("\"bluetooth_enabled\":true", json);

        var roundTripped = JsonSerializer.Deserialize<PicoNetworkConfig>(json, Options);
        Assert.Equal(config, roundTripped);
    }
}
