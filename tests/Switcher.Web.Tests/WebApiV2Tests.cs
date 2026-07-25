using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.TestHost;
using Switcher.Contracts;

namespace Switcher.Web.Tests;

public class WebApiV2Tests
{
    [Fact]
    public async Task PostSources_WithValidDefinition_ReturnsNoContentAndDelegatesToService()
    {
        var configService = new FakeSwitcherConfigService();
        using var host = await TestWebHostFactory.CreateAsync(configService, new FakeInputSourceManager([]), new RecordingControllerInputSink());
        using var client = host.GetTestClient();

        var source = new SourceDefinition("src-ndi-cam1", "Cam 1 (NDI)", SourceType.Ndi, new NdiConfig("STUDIO (Cam1)"), null, null);
        var response = await client.PostAsJsonAsync("/api/v1/sources", source, ProtocolJsonOptions.Default);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal("src-ndi-cam1", Assert.Single(configService.AddedSources).Id);
    }

    [Fact]
    public async Task PostSources_WithoutTypeSpecificConfig_ReturnsBadRequest()
    {
        var configService = new FakeSwitcherConfigService();
        using var host = await TestWebHostFactory.CreateAsync(configService, new FakeInputSourceManager([]), new RecordingControllerInputSink());
        using var client = host.GetTestClient();

        var source = new SourceDefinition("src-ndi-cam1", "Cam 1 (NDI)", SourceType.Ndi, null, null, null);
        var response = await client.PostAsJsonAsync("/api/v1/sources", source, ProtocolJsonOptions.Default);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(configService.AddedSources);
    }

    [Fact]
    public async Task PutSources_DelegatesIdAndDefinitionToService()
    {
        var configService = new FakeSwitcherConfigService();
        using var host = await TestWebHostFactory.CreateAsync(configService, new FakeInputSourceManager([]), new RecordingControllerInputSink());
        using var client = host.GetTestClient();

        var source = new SourceDefinition("src-ndi-cam1", "Cam 1 renamed", SourceType.Ndi, new NdiConfig("STUDIO (Cam1)"), null, null);
        var response = await client.PutAsJsonAsync("/api/v1/sources/src-ndi-cam1", source, ProtocolJsonOptions.Default);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var (id, updated) = Assert.Single(configService.UpdatedSources);
        Assert.Equal("src-ndi-cam1", id);
        Assert.Equal("Cam 1 renamed", updated.Name);
    }

    [Fact]
    public async Task DeleteSources_DelegatesIdToService()
    {
        var configService = new FakeSwitcherConfigService();
        using var host = await TestWebHostFactory.CreateAsync(configService, new FakeInputSourceManager([]), new RecordingControllerInputSink());
        using var client = host.GetTestClient();

        var response = await client.DeleteAsync("/api/v1/sources/src-ndi-cam1");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal("src-ndi-cam1", Assert.Single(configService.RemovedSourceIds));
    }

    [Fact]
    public async Task PostProgram_WithValidRequest_ReturnsNoContentAndDelegatesToService()
    {
        var configService = new FakeSwitcherConfigService();
        using var host = await TestWebHostFactory.CreateAsync(configService, new FakeInputSourceManager([]), new RecordingControllerInputSink());
        using var client = host.GetTestClient();

        var request = new ProgramRequest(
            ProgramBus.Pgm1,
            [new ProgramLayer("src-ndi-cam1", new PipSettings(true, 0, 0, 1920, 1080, 1.0, 0, null))],
            Take: true);
        var response = await client.PostAsJsonAsync("/api/v1/program", request, ProtocolJsonOptions.Default);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.True(Assert.Single(configService.AppliedPrograms).Take);
    }

    [Fact]
    public async Task PostProgram_WithMissingLayerSourceId_ReturnsBadRequest()
    {
        var configService = new FakeSwitcherConfigService();
        using var host = await TestWebHostFactory.CreateAsync(configService, new FakeInputSourceManager([]), new RecordingControllerInputSink());
        using var client = host.GetTestClient();

        var request = new ProgramRequest(
            ProgramBus.Pgm1,
            [new ProgramLayer("", new PipSettings(true, 0, 0, 1920, 1080, 1.0, 0, null))],
            Take: false);
        var response = await client.PostAsJsonAsync("/api/v1/program", request, ProtocolJsonOptions.Default);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(configService.AppliedPrograms);
    }

    [Fact]
    public async Task PutMultiview_WithSixteenValidCells_ReturnsNoContentAndDelegatesToService()
    {
        var configService = new FakeSwitcherConfigService();
        using var host = await TestWebHostFactory.CreateAsync(configService, new FakeInputSourceManager([]), new RecordingControllerInputSink());
        using var client = host.GetTestClient();

        string[] cells =
        [
            "PGM1", "PGM2", "PVW1", "PVW2",
            "SRC:src-ndi-cam1", "SRC:src-webcam-1", "EMPTY", "EMPTY",
            "EMPTY", "EMPTY", "EMPTY", "EMPTY",
            "EMPTY", "EMPTY", "EMPTY", "EMPTY",
        ];
        var response = await client.PutAsJsonAsync("/api/v1/multiview", new MultiviewLayout(cells), ProtocolJsonOptions.Default);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(16, Assert.Single(configService.AppliedMultiviews).Cells.Count);
    }

    [Fact]
    public async Task PutMultiview_WithWrongCellCount_ReturnsBadRequest()
    {
        var configService = new FakeSwitcherConfigService();
        using var host = await TestWebHostFactory.CreateAsync(configService, new FakeInputSourceManager([]), new RecordingControllerInputSink());
        using var client = host.GetTestClient();

        var response = await client.PutAsJsonAsync("/api/v1/multiview", new MultiviewLayout(["PGM1"]), ProtocolJsonOptions.Default);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(configService.AppliedMultiviews);
    }

    [Fact]
    public async Task PutOutputs_WithValidRequest_ReturnsNoContentAndDelegatesToService()
    {
        var configService = new FakeSwitcherConfigService();
        using var host = await TestWebHostFactory.CreateAsync(configService, new FakeInputSourceManager([]), new RecordingControllerInputSink());
        using var client = host.GetTestClient();

        var request = new OutputsRequest(
        [
            new OutputAssignment(OutputSink.Vcam1, OutputSource.Pgm1, null, null, null),
            new OutputAssignment(OutputSink.Hdmi, OutputSource.Pgm2, 1, true, true),
        ]);
        var response = await client.PutAsJsonAsync("/api/v1/outputs", request, ProtocolJsonOptions.Default);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(2, Assert.Single(configService.AppliedOutputs).Outputs.Count);
    }

    [Fact]
    public async Task PutOutputs_WithHdmiMissingDisplayId_ReturnsBadRequest()
    {
        var configService = new FakeSwitcherConfigService();
        using var host = await TestWebHostFactory.CreateAsync(configService, new FakeInputSourceManager([]), new RecordingControllerInputSink());
        using var client = host.GetTestClient();

        var request = new OutputsRequest([new OutputAssignment(OutputSink.Hdmi, OutputSource.Pgm1, null, null, null)]);
        var response = await client.PutAsJsonAsync("/api/v1/outputs", request, ProtocolJsonOptions.Default);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(configService.AppliedOutputs);
    }

    [Fact]
    public async Task PutModules_WithValidRequest_ReturnsNoContentAndDelegatesToService()
    {
        var configService = new FakeSwitcherConfigService();
        using var host = await TestWebHostFactory.CreateAsync(configService, new FakeInputSourceManager([]), new RecordingControllerInputSink());
        using var client = host.GetTestClient();

        var request = new ModulesRequest(
        [
            new ModuleMapping(0, new ModuleSourceBinding("src-ndi-cam1", "transition"), new ModuleSourceBinding("src-webcam-1", "opacity")),
        ]);
        var response = await client.PutAsJsonAsync("/api/v1/modules", request, ProtocolJsonOptions.Default);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Single(configService.AppliedModules);
    }

    [Fact]
    public async Task PutModules_WithOutOfRangeIndex_ReturnsBadRequest()
    {
        var configService = new FakeSwitcherConfigService();
        using var host = await TestWebHostFactory.CreateAsync(configService, new FakeInputSourceManager([]), new RecordingControllerInputSink());
        using var client = host.GetTestClient();

        var request = new ModulesRequest(
        [
            new ModuleMapping(ProtocolConstants.MaxModules, new ModuleSourceBinding(null, "transition"), new ModuleSourceBinding(null, "opacity")),
        ]);
        var response = await client.PutAsJsonAsync("/api/v1/modules", request, ProtocolJsonOptions.Default);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(configService.AppliedModules);
    }

    [Fact]
    public async Task PutAtem_WithValidConfig_ReturnsNoContentAndDelegatesToService()
    {
        var configService = new FakeSwitcherConfigService();
        using var host = await TestWebHostFactory.CreateAsync(configService, new FakeInputSourceManager([]), new RecordingControllerInputSink());
        using var client = host.GetTestClient();

        var config = new AtemConfig(
            true,
            "192.168.1.240",
            [new AtemButtonMapping("main", 0, "PGM1xSRC1", "ProgramInput", 0, 1)]);
        var response = await client.PutAsJsonAsync("/api/v1/atem", config, ProtocolJsonOptions.Default);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal("192.168.1.240", Assert.Single(configService.AppliedAtemConfigs).Ip);
    }

    [Fact]
    public async Task PostAtemCommand_WithValidRequest_ReturnsNoContentAndDelegatesToService()
    {
        var configService = new FakeSwitcherConfigService();
        using var host = await TestWebHostFactory.CreateAsync(configService, new FakeInputSourceManager([]), new RecordingControllerInputSink());
        using var client = host.GetTestClient();

        var command = new AtemCommandRequest("Cut", 0, 1);
        var response = await client.PostAsJsonAsync("/api/v1/atem/command", command, ProtocolJsonOptions.Default);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal("Cut", Assert.Single(configService.SentAtemCommands).Action);
    }

    [Fact]
    public async Task PutPicoNetwork_WithValidConfig_ReturnsNoContentAndDelegatesToService()
    {
        var configService = new FakeSwitcherConfigService();
        using var host = await TestWebHostFactory.CreateAsync(configService, new FakeInputSourceManager([]), new RecordingControllerInputSink());
        using var client = host.GetTestClient();

        var config = new PicoNetworkConfig("MyWifi", "hunter2", "main", BluetoothEnabled: false);
        var response = await client.PutAsJsonAsync("/api/v1/pico/network", config, ProtocolJsonOptions.Default);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal("main", Assert.Single(configService.AppliedPicoNetworkConfigs).ControllerId);
    }
}
