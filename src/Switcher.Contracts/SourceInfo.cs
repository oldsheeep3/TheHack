namespace Switcher.Contracts;

public sealed record SourceInfo(
    int Channel,
    string Name,
    SourceProtocol Protocol,
    string? Resolution,
    SourceStatus Status);
