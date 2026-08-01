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

    /// <summary>
    /// Adds the protocol's converters to <paramref name="options"/>, for hosts that own their own
    /// <see cref="JsonSerializerOptions"/> (the ASP.NET Core <c>JsonOptions</c>) and so cannot simply use
    /// <see cref="Default"/>.
    /// <para>
    /// Order matters. A converter in <see cref="JsonSerializerOptions.Converters"/> outranks a
    /// <c>[JsonConverter]</c> attribute on the type, so a bare <see cref="JsonStringEnumConverter"/> —
    /// which claims every enum — would shadow <see cref="OutputSinkJsonConverter"/> and with it the
    /// ability to read the ordinal-less <c>"HDMI"</c> token an older build wrote. Registering the
    /// specific converter first is what keeps that upgrade path working.
    /// </para>
    /// </summary>
    public static void AddConverters(JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.Converters.Add(new OutputSinkJsonConverter());
        options.Converters.Add(new JsonStringEnumConverter());
    }

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        };
        AddConverters(options);
        return options;
    }
}
