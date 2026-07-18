using Switcher.Contracts;

namespace Switcher.Media;

/// <summary>One program bus's preview layer settings, keyed by channel (the same key
/// <see cref="Compositing.PipLayoutCalculator"/> and <see cref="CompositedLayer"/> use).</summary>
public sealed record BusSceneState(ProgramBus Bus, IReadOnlyDictionary<int, PipSettings> PreviewLayers);

/// <summary>Serializable snapshot of both program buses' preview state, for scene preset save/load
/// (docs/specs/pc-switcher-app.md §2.2). Persistence itself (to disk/DB) is the App layer's
/// responsibility; this is just the DTO <see cref="CompositorEngine.GetSceneSnapshot"/> /
/// <see cref="CompositorEngine.ApplySceneSnapshot"/> exchange.</summary>
public sealed record SceneSnapshot(IReadOnlyList<BusSceneState> Buses);
