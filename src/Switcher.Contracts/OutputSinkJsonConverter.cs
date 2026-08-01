using System.Text.Json;
using System.Text.Json.Serialization;

namespace Switcher.Contracts;

/// <summary>
/// Reads and writes <see cref="OutputSink"/> as its wire token, accepting the tokens written by builds
/// that predate multiple sinks per kind.
/// <para>
/// Before the output table became operator-editable there was exactly one display sink and its token was
/// <c>"HDMI"</c> with no ordinal. That token is still sitting in every <c>runtime-config.json</c> written
/// by those builds, and an operator upgrading must not lose their routing, so it is read as
/// <see cref="OutputSink.Hdmi1"/>. Writing always emits the canonical ordinal form, which is how the
/// legacy token disappears from a config the first time it is saved.
/// </para>
/// </summary>
public sealed class OutputSinkJsonConverter : JsonConverter<OutputSink>
{
    public override OutputSink Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException($"Expected a string output sink token but found {reader.TokenType}.");
        }

        var token = reader.GetString();
        return Parse(token) ?? throw new JsonException($"Unknown output sink token '{token}'.");
    }

    public override void Write(Utf8JsonWriter writer, OutputSink value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteStringValue(OutputCatalog.TokenOf(value));
    }

    /// <summary>The sink named by <paramref name="token"/> (case-insensitive, legacy tokens included),
    /// or <c>null</c> when it names no sink.</summary>
    public static OutputSink? Parse(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        var normalized = token.Trim().ToUpperInvariant();

        // Ordinal-less legacy tokens: the first sink of that kind.
        normalized = normalized switch
        {
            "HDMI" => "HDMI1",
            "VCAM" or "WEBCAM" => "VCAM1",
            "NDI" => "NDI1",
            _ => normalized,
        };

        foreach (var sink in Enum.GetValues<OutputSink>())
        {
            if (OutputCatalog.TokenOf(sink) == normalized)
            {
                return sink;
            }
        }

        return null;
    }
}
