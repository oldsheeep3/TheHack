using Switcher.Contracts;
using Switcher.Tools.ModuleSimulator;

// Debug tool: drives a -DENABLE_FAKE_MODULES=ON build of the pico2w-controller firmware from a
// browser, standing in for the physical CH32V003 switching modules. See README.md.

var builder = WebApplication.CreateBuilder(args);
builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
builder.Services.AddSingleton<PicoDebugDevice>();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

var device = app.Services.GetRequiredService<PicoDebugDevice>();

app.MapGet("/api/state", () => Results.Json(device.Snapshot()));

app.MapGet("/api/diag", () => Results.Json(device.Diagnostics()));

app.MapGet("/api/config", () => Results.Json(new
{
    maxModules = ProtocolConstants.MaxModules,
    backlightsPerModule = ProtocolConstants.BacklightsPerModule,
}));

app.MapPost("/api/modules/{index:int}", (int index, SetModuleRequest req) =>
{
    if (index < 0 || index >= ProtocolConstants.MaxModules)
    {
        return Results.BadRequest($"module index must be 0..{ProtocolConstants.MaxModules - 1}");
    }

    try
    {
        device.SetModule(index, req.Present, req.Pgm1Src1, req.Pgm1Src2, req.Pgm2Src1, req.Pgm2Src2,
            req.VrSrc1, req.VrSrc2);
        return Results.Ok();
    }
    catch (Exception ex)
    {
        // The Pico may have been unplugged between the state poll and this write; the read loop
        // reconnects on its own, so surface it to the UI rather than crashing the tool.
        return Results.Problem(ex.Message, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});

app.MapPost("/api/modules/{index:int}/backlight", (int index, SetBacklightRequest req) =>
{
    try
    {
        device.SetBacklight(index, req.Colors);
        return Results.Ok();
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(ex.Message);
    }
    catch (Exception ex)
    {
        return Results.Problem(ex.Message, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});

app.MapPost("/api/reboot-bootsel", () =>
{
    try
    {
        device.RebootToBootsel();
        return Results.Ok();
    }
    catch (Exception ex)
    {
        return Results.Problem(ex.Message, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});

app.Lifetime.ApplicationStopping.Register(device.Dispose);

var url = "http://127.0.0.1:5199";
app.Urls.Add(url);
Console.WriteLine($"module-simulator: open {url}");
app.Run();
