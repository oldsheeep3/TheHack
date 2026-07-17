using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Switcher.Contracts;

namespace Switcher.Web;

/// <summary>
/// Registers the services shared by the production Kestrel <see cref="WebHost"/> and the test host,
/// so both build the exact same dependency graph.
/// </summary>
internal static class WebHostServices
{
    public static void Configure(
        IServiceCollection services,
        ISwitcherConfigService configService,
        IInputSourceManager sourceManager,
        IControllerInputSink inputSink)
    {
        services.AddLogging();
        services.AddSingleton(configService);
        services.AddSingleton(sourceManager);
        services.AddSingleton(inputSink);
        services.AddSingleton<ControllerInputQueue>();

        // Match the snake_case + string-enum wire format used across the whole protocol
        // (see Switcher.Contracts.ProtocolJsonOptions).
        services.Configure<JsonOptions>(options =>
        {
            options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
        });
    }
}
