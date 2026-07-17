using System.Text.Json.Serialization;

namespace Switcher.Contracts;

public sealed record PipSettings(
    bool Enabled,
    [property: JsonPropertyName("x_position")] int X,
    [property: JsonPropertyName("y_position")] int Y,
    int Width,
    int Height,
    double Opacity,
    int ZOrder,
    CropRect? Crop);
