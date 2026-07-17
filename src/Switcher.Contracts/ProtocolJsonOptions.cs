using System.Text.Json;
using System.Text.Json.Serialization;

namespace Switcher.Contracts;

/// <summary>
/// Shared <see cref="JsonSerializerOptions"/> so every module serializes contract types identically:
/// snake_case property names and string-valued enums, matching docs/specs/00-system-overview.md §4.
/// </summary>
public static class ProtocolJsonOptions
{
    public static JsonSerializerOptions Default { get; } = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
