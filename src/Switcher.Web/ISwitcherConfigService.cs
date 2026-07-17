using Switcher.Contracts;

namespace Switcher.Web;

/// <summary>
/// Facade bundling the core services (<see cref="ICompositorEngine"/> / <see cref="IInputSourceManager"/>)
/// that <c>POST /api/v1/config</c> delegates to. Implemented by the App integration layer and injected
/// into <see cref="WebHost"/>; the Web layer never references the core service implementations directly.
/// </summary>
public interface ISwitcherConfigService
{
    Task ApplyConfigAsync(ConfigChangeRequest request, CancellationToken cancellationToken = default);
}
