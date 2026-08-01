using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.TestHost;
using Switcher.Contracts;

namespace Switcher.Web.Tests;

/// <summary>
/// The read-back half of the settings API. Every mutating endpoint here replaces the whole table it
/// owns, so a settings client that could not read the current one first could only overwrite it — which
/// is what these routes exist to prevent (docs/specs/phone-web-bridge.md §2.2).
/// </summary>
public class ConfigReadbackEndpointTests
{
    [Fact]
    public async Task GetSourceDefinitions_ReturnsThePerTypeConfigTheListEndpointOmits()
    {
        var configService = new FakeSwitcherConfigService();
        configService.CurrentSourceDefinitions.Add(
            new SourceDefinition("src-ndi-cam1", "Cam 1", SourceType.Ndi, new NdiConfig("STUDIO (Cam1)"), null, null));
        using var host = await TestWebHostFactory.CreateAsync(
            configService, new FakeInputSourceManager([]), new RecordingControllerInputSink());
        using var client = host.GetTestClient();

        var response = await client.GetAsync("/api/v1/sources/definitions");
        var definitions = await response.Content.ReadFromJsonAsync<List<SourceDefinition>>(ProtocolJsonOptions.Default);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var definition = Assert.Single(definitions!);
        Assert.Equal("src-ndi-cam1", definition.Id);
        Assert.Equal("STUDIO (Cam1)", definition.Ndi!.SourceName);
    }

    [Fact]
    public async Task GetSourceDefinitions_IsNotMatchedAsASourceId()
    {
        // "definitions" is a literal segment sharing a prefix with PUT/DELETE /api/v1/sources/{id};
        // if routing preferred the parameter, this route would never be reachable.
        var configService = new FakeSwitcherConfigService();
        using var host = await TestWebHostFactory.CreateAsync(
            configService, new FakeInputSourceManager([]), new RecordingControllerInputSink());
        using var client = host.GetTestClient();

        var response = await client.GetAsync("/api/v1/sources/definitions");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(configService.RemovedSourceIds);
    }

    [Fact]
    public async Task GetMultiview_ReturnsTheLayoutInForce()
    {
        var configService = new FakeSwitcherConfigService
        {
            CurrentMultiviewLayout = new MultiviewLayout(
                [],
                new MultiviewGrid(4, 4),
                [new MultiviewRegion(0, 0, 2, 2, "PGM1")]),
        };
        using var host = await TestWebHostFactory.CreateAsync(
            configService, new FakeInputSourceManager([]), new RecordingControllerInputSink());
        using var client = host.GetTestClient();

        var layout = await client.GetFromJsonAsync<MultiviewLayout>("/api/v1/multiview", ProtocolJsonOptions.Default);

        Assert.Equal(4, layout!.Grid!.Rows);
        Assert.Equal("PGM1", Assert.Single(layout.Regions!).Content);
    }

    [Fact]
    public async Task GetOutputs_ReturnsTheTableInForce()
    {
        var configService = new FakeSwitcherConfigService();
        using var host = await TestWebHostFactory.CreateAsync(
            configService, new FakeInputSourceManager([]), new RecordingControllerInputSink());
        using var client = host.GetTestClient();

        var outputs = await client.GetFromJsonAsync<OutputsRequest>("/api/v1/outputs", ProtocolJsonOptions.Default);

        Assert.Equal(OutputDefaults.Default.Count, outputs!.Outputs.Count);
        Assert.Contains(outputs.Outputs, output => output.Sink == OutputSink.Vcam1);
    }

    [Fact]
    public async Task GetAudioOutputs_ReturnsTheRoutingInForce()
    {
        var configService = new FakeSwitcherConfigService();
        configService.CurrentAudioOutputs.Add(new AudioOutputAssignment(ProgramBus.Pgm1, "{0.0.0.1}", "Speakers"));
        using var host = await TestWebHostFactory.CreateAsync(
            configService, new FakeInputSourceManager([]), new RecordingControllerInputSink());
        using var client = host.GetTestClient();

        var audio = await client.GetFromJsonAsync<AudioOutputsRequest>(
            "/api/v1/audio/outputs", ProtocolJsonOptions.Default);

        var assignment = Assert.Single(audio!.Outputs);
        Assert.Equal(ProgramBus.Pgm1, assignment.Bus);
        Assert.Equal("Speakers", assignment.DeviceName);
    }

    [Fact]
    public async Task GetModules_ReturnsTheBindingsInForce()
    {
        var configService = new FakeSwitcherConfigService();
        configService.CurrentModules.Add(new ModuleMapping(
            0,
            new ModuleSourceBinding("src-ndi-cam1", "transition"),
            new ModuleSourceBinding(null, "opacity")));
        using var host = await TestWebHostFactory.CreateAsync(
            configService, new FakeInputSourceManager([]), new RecordingControllerInputSink());
        using var client = host.GetTestClient();

        var modules = await client.GetFromJsonAsync<ModulesRequest>("/api/v1/modules", ProtocolJsonOptions.Default);

        var mapping = Assert.Single(modules!.Modules);
        Assert.Equal("src-ndi-cam1", mapping.Src1.SourceId);
        Assert.Null(mapping.Src2.SourceId);
    }
}
