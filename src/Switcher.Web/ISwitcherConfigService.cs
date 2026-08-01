using Switcher.Contracts;

namespace Switcher.Web;

/// <summary>
/// Facade bundling the core mutations (backed by <see cref="IVideoEngine"/> in the App layer) that the
/// <c>/api/v1/*</c> endpoints delegate to. Implemented by the App integration layer and injected into
/// <see cref="WebHost"/>; the Web layer never references the core service implementations directly.
/// </summary>
public interface ISwitcherConfigService
{
    Task ApplyConfigAsync(ConfigChangeRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// The source definitions currently in force, with the per-type config each was created from.
    /// <para>
    /// <c>GET /api/v1/sources</c> reports <see cref="SourceInfo"/> — channel, name, protocol, status —
    /// which is what an operator console needs but not what a settings client needs: it cannot show an
    /// edit form for an NDI source without the NDI name, and a client that guesses would silently
    /// replace the missing halves on the next <c>PUT</c>.
    /// </para>
    /// </summary>
    Task<IReadOnlyList<SourceDefinition>> GetSourceDefinitionsAsync(CancellationToken cancellationToken = default);

    Task AddSourceAsync(SourceDefinition source, CancellationToken cancellationToken = default);

    Task UpdateSourceAsync(string id, SourceDefinition source, CancellationToken cancellationToken = default);

    Task RemoveSourceAsync(string id, CancellationToken cancellationToken = default);

    Task ApplyProgramAsync(ProgramRequest request, CancellationToken cancellationToken = default);

    Task ApplyMultiviewAsync(MultiviewLayout layout, CancellationToken cancellationToken = default);

    /// <summary>The multiview layout currently in force, in whichever form it was applied.</summary>
    Task<MultiviewLayout> GetMultiviewLayoutAsync(CancellationToken cancellationToken = default);

    Task ApplyOutputsAsync(OutputsRequest request, CancellationToken cancellationToken = default);

    /// <summary>The output table currently in force. Each <c>PUT</c> replaces the whole table, so a
    /// client that cannot read it first can only overwrite it.</summary>
    Task<OutputsRequest> GetOutputsAsync(CancellationToken cancellationToken = default);

    /// <summary>Routes program buses to audio output devices, replacing the whole table.</summary>
    Task ApplyAudioOutputsAsync(AudioOutputsRequest request, CancellationToken cancellationToken = default);

    /// <summary>The bus-to-device audio routing currently in force.</summary>
    Task<AudioOutputsRequest> GetAudioOutputsAsync(CancellationToken cancellationToken = default);

    Task ApplyModulesAsync(ModulesRequest request, CancellationToken cancellationToken = default);

    /// <summary>The module bindings currently in force.</summary>
    Task<ModulesRequest> GetModulesAsync(CancellationToken cancellationToken = default);

    Task ApplyAtemConfigAsync(AtemConfig config, CancellationToken cancellationToken = default);

    /// <summary>The ATEM connection settings and button mappings currently in force.</summary>
    Task<AtemConfig> GetAtemConfigAsync(CancellationToken cancellationToken = default);

    /// <summary>Sweeps the local network for ATEM switchers the operator can pick from.</summary>
    Task<IReadOnlyList<AtemDeviceInfo>> DiscoverAtemDevicesAsync(CancellationToken cancellationToken = default);

    /// <summary>Points the connected ATEM's streaming output at a URL. False when nothing is connected.</summary>
    Task<bool> ConfigureAtemStreamingAsync(AtemStreamingRequest request, CancellationToken cancellationToken = default);

    Task SendAtemCommandAsync(AtemCommandRequest command, CancellationToken cancellationToken = default);

    Task ApplyPicoNetworkConfigAsync(PicoNetworkConfig config, CancellationToken cancellationToken = default);
}
