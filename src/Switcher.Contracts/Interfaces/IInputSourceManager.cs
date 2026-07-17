namespace Switcher.Contracts;

/// <summary>
/// Manages the lifecycle and status of all input sources (UVC / NDI / SRT).
/// </summary>
public interface IInputSourceManager
{
    IReadOnlyList<SourceInfo> GetSources();

    void AddSource(int channel, SourceProtocol protocol, string? sourceUrl);

    void RemoveSource(int channel);

    event EventHandler<SourceInfo> SourceStatusChanged;
}
