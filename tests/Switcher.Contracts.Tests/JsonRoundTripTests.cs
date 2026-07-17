using System.Text.Json;
using Switcher.Contracts;

namespace Switcher.Contracts.Tests;

public class JsonRoundTripTests
{
    private static readonly JsonSerializerOptions Options = ProtocolJsonOptions.Default;

    [Fact]
    public void TallyState_SerializesWithSpecFieldNames()
    {
        var state = new TallyState(new[] { 1, 5 }, new[] { 2 });

        var json = JsonSerializer.Serialize(state, Options);

        Assert.Contains("\"active_pgm\":[1,5]", json);
        Assert.Contains("\"active_pvw\":[2]", json);

        var roundTripped = JsonSerializer.Deserialize<TallyState>(json, Options);
        Assert.Equal(state.ActivePgm, roundTripped?.ActivePgm);
        Assert.Equal(state.ActivePvw, roundTripped?.ActivePvw);
    }

    [Fact]
    public void WsEnvelope_SerializesWithSpecFieldNames()
    {
        var envelope = new WsEnvelope("button_press", new ButtonEvent("main", 3, 1773663861000));

        var json = JsonSerializer.Serialize(envelope, Options);

        Assert.Contains("\"event\":\"button_press\"", json);
        Assert.Contains("\"controller_id\":\"main\"", json);
        Assert.Contains("\"button_id\":3", json);
        Assert.Contains("\"timestamp\":1773663861000", json);

        var roundTripped = JsonSerializer.Deserialize<WsEnvelope>(json, Options);
        Assert.Equal(envelope, roundTripped);
    }

    [Fact]
    public void ConfigChangeRequest_SerializesWithSpecFieldNames()
    {
        var request = new ConfigChangeRequest(
            2,
            SourceProtocol.Srt,
            "srt://192.168.1.100:9000?mode=caller",
            new PipSettings(true, 1420, 80, 480, 270, 1.0, 0, null));

        var json = JsonSerializer.Serialize(request, Options);

        Assert.Contains("\"target_channel\":2", json);
        Assert.Contains("\"source_type\":\"SRT\"", json);
        Assert.Contains("\"source_url\":", json);
        Assert.Contains("\"pip_settings\":", json);
        Assert.Contains("\"x_position\":1420", json);
        Assert.Contains("\"y_position\":80", json);

        var roundTripped = JsonSerializer.Deserialize<ConfigChangeRequest>(json, Options);
        Assert.Equal(request, roundTripped);
    }

    [Fact]
    public void SourceInfo_RoundTrips()
    {
        var source = new SourceInfo(1, "cam-1", SourceProtocol.Uvc, "1920x1080", SourceStatus.Connected);

        var json = JsonSerializer.Serialize(source, Options);
        var roundTripped = JsonSerializer.Deserialize<SourceInfo>(json, Options);

        Assert.Equal(source, roundTripped);
    }
}
