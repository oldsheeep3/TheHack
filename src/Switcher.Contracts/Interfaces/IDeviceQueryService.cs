namespace Switcher.Contracts;

/// <summary>
/// Enumerates available input devices and reports SRT setup guidance for the local PC
/// (docs/specs/multiview-output-revision.md §4.1). Implemented by Media, consumed by the Web layer,
/// and wired to the concrete implementation by the App host.
/// </summary>
public interface IDeviceQueryService
{
    Task<IReadOnlyList<DeviceInfo>> EnumerateAsync(DeviceQueryType type, CancellationToken ct = default);

    Task<SrtSetupInfo> GetSrtSetupAsync(CancellationToken ct = default);
}
