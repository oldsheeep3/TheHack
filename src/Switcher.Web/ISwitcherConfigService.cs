using Switcher.Contracts;

namespace Switcher.Web;

/// <summary>
/// Facade bundling the core services (<see cref="ICompositorEngine"/> / <see cref="IInputSourceManager"/>)
/// that the <c>/api/v1/*</c> endpoints delegate to. Implemented by the App integration layer
/// (agent-A2-006) and injected into <see cref="WebHost"/>; the Web layer never references the core
/// service implementations directly.
/// </summary>
public interface ISwitcherConfigService
{
    Task ApplyConfigAsync(ConfigChangeRequest request, CancellationToken cancellationToken = default);

    Task AddSourceAsync(SourceDefinition source, CancellationToken cancellationToken = default);

    Task UpdateSourceAsync(string id, SourceDefinition source, CancellationToken cancellationToken = default);

    Task RemoveSourceAsync(string id, CancellationToken cancellationToken = default);

    Task ApplyProgramAsync(ProgramRequest request, CancellationToken cancellationToken = default);

    Task ApplyMultiviewAsync(MultiviewLayout layout, CancellationToken cancellationToken = default);

    Task ApplyOutputsAsync(OutputsRequest request, CancellationToken cancellationToken = default);

    Task ApplyModulesAsync(ModulesRequest request, CancellationToken cancellationToken = default);

    Task ApplyAtemConfigAsync(AtemConfig config, CancellationToken cancellationToken = default);

    Task SendAtemCommandAsync(AtemCommandRequest command, CancellationToken cancellationToken = default);

    Task ApplyPicoNetworkConfigAsync(PicoNetworkConfig config, CancellationToken cancellationToken = default);
}
