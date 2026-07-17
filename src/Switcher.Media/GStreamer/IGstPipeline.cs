using Switcher.Contracts;

namespace Switcher.Media.GStreamer;

/// <summary>
/// Abstraction over a single GStreamer capture pipeline, so <see cref="Switcher.Media.InputSource"/>
/// can be unit tested (state transitions, reconnect backoff) without a native GStreamer runtime.
/// </summary>
internal interface IGstPipeline : IDisposable
{
    /// <summary>Builds and starts the underlying pipeline. Throws if the pipeline cannot be created.</summary>
    void Start();

    /// <summary>Stops and tears down the underlying pipeline. Safe to call multiple times.</summary>
    void Stop();

    /// <summary>Raised on the pipeline's own thread whenever a new decoded frame is available.</summary>
    event EventHandler<FrameData>? FrameReady;

    /// <summary>Raised when the pipeline reports an unrecoverable error or reaches end-of-stream.</summary>
    event EventHandler<string>? Faulted;
}

/// <summary>Creates <see cref="IGstPipeline"/> instances for a given protocol/source URL.</summary>
internal interface IGstPipelineFactory
{
    IGstPipeline Create(SourceProtocol protocol, string? sourceUrl);
}

/// <summary>Builds real <see cref="GstAppSinkPipeline"/> instances from <see cref="PipelineDescriptorFactory"/>.</summary>
internal sealed class GstPipelineFactory : IGstPipelineFactory
{
    public IGstPipeline Create(SourceProtocol protocol, string? sourceUrl) =>
        new GstAppSinkPipeline(PipelineDescriptorFactory.Build(protocol, sourceUrl));
}
