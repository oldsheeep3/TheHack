using Switcher.Contracts;
using Switcher.Media.Compositing;
using Switcher.Media.GStreamer;

namespace Switcher.Media.Tests;

/// <summary>Controllable stand-in for a real GStreamer pipeline: tests raise <see cref="FrameReady"/>
/// / <see cref="Faulted"/> directly instead of needing a native GStreamer runtime.</summary>
internal sealed class FakeGstPipeline : IGstPipeline
{
    public int StartCount { get; private set; }
    public int StopCount { get; private set; }
    public bool ThrowOnStart { get; set; }

    public event EventHandler<FrameData>? FrameReady;
    public event EventHandler<string>? Faulted;

    public void Start()
    {
        StartCount++;
        if (ThrowOnStart)
        {
            throw new InvalidOperationException("Simulated pipeline start failure.");
        }
    }

    public void Stop() => StopCount++;

    public void RaiseFrame(FrameData frame) => FrameReady?.Invoke(this, frame);

    public void RaiseFaulted(string reason) => Faulted?.Invoke(this, reason);

    public void Dispose()
    {
    }
}

/// <summary>Records every <see cref="Compose"/> call's layer list so tests can assert on scene
/// contents/order without a real DirectX 11 device.</summary>
internal sealed class FakeGpuCompositor : IGpuCompositor
{
    public List<IReadOnlyList<CompositedLayer>> ComposeCalls { get; } = [];

    public int DisposeCount { get; private set; }

    public FrameData Compose(IReadOnlyList<CompositedLayer> layers, Func<int, FrameData?> frameLookup, int canvasWidth, int canvasHeight)
    {
        ComposeCalls.Add(layers);
        return new FrameData(canvasWidth, canvasHeight, Array.Empty<byte>());
    }

    public void Dispose() => DisposeCount++;
}

internal sealed class FakeFrameSource : IFrameSource
{
    public Dictionary<string, int> ChannelsById { get; } = new();

    public bool TryGetLatestFrame(int channel, out FrameData? frame)
    {
        frame = null;
        return false;
    }

    public bool TryResolveChannel(string sourceId, out int channel) => ChannelsById.TryGetValue(sourceId, out channel);
}
