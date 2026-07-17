using System.Collections.Concurrent;
using Microsoft.Extensions.Logging.Abstractions;
using Switcher.Contracts;
using Switcher.Media.GStreamer;

namespace Switcher.Media.Tests;

public class InputSourceTests
{
    private static readonly TimeSpan FastInitialBackoff = TimeSpan.FromMilliseconds(20);
    private static readonly TimeSpan FastMaxBackoff = TimeSpan.FromMilliseconds(100);

    [Fact]
    public async Task Start_WhenPipelineProducesAFrame_TransitionsToConnectedAndExposesTheFrame()
    {
        var pipelines = new ConcurrentQueue<FakeGstPipeline>();
        IGstPipeline Factory(SourceProtocol protocol, string? url)
        {
            var pipeline = new FakeGstPipeline();
            pipelines.Enqueue(pipeline);
            return pipeline;
        }

        using var source = new InputSource(1, SourceProtocol.Srt, null, Factory, NullLogger.Instance, FastInitialBackoff, FastMaxBackoff);
        Assert.Equal(SourceStatus.Disconnected, source.Info.Status);

        source.Start();

        await WaitUntilAsync(() => pipelines.Count >= 1);
        await WaitUntilAsync(() => source.Info.Status == SourceStatus.Connected);

        pipelines.TryPeek(out var pipeline);
        var frame = new FrameData(640, 480, new byte[4]);
        pipeline!.RaiseFrame(frame);

        await WaitUntilAsync(() => source.LatestFrame is not null);
        Assert.Equal(frame, source.LatestFrame);
        Assert.Equal("640x480", source.Info.Resolution);
    }

    [Fact]
    public async Task Start_WhenPipelineFaults_TransitionsToErrorThenReconnectsWithANewPipeline()
    {
        var pipelines = new ConcurrentQueue<FakeGstPipeline>();
        IGstPipeline Factory(SourceProtocol protocol, string? url)
        {
            var pipeline = new FakeGstPipeline();
            pipelines.Enqueue(pipeline);
            return pipeline;
        }

        using var source = new InputSource(2, SourceProtocol.Ndi, "cam-1", Factory, NullLogger.Instance, FastInitialBackoff, FastMaxBackoff);
        source.Start();

        await WaitUntilAsync(() => pipelines.Count >= 1);
        pipelines.TryPeek(out var first);
        first!.RaiseFaulted("simulated disconnect");

        await WaitUntilAsync(() => source.Info.Status == SourceStatus.Error);
        await WaitUntilAsync(() => pipelines.Count >= 2);

        Assert.True(first.StopCount >= 1, "The faulted pipeline must be torn down before reconnecting.");
    }

    [Fact]
    public async Task Start_WhenPipelineThrowsOnStart_IsIsolatedAndStillBacksOffAndRetries()
    {
        var attempts = 0;
        IGstPipeline Factory(SourceProtocol protocol, string? url)
        {
            attempts++;
            return new FakeGstPipeline { ThrowOnStart = attempts == 1 };
        }

        using var source = new InputSource(3, SourceProtocol.Uvc, null, Factory, NullLogger.Instance, FastInitialBackoff, FastMaxBackoff);
        source.Start();

        await WaitUntilAsync(() => attempts >= 2);
        await WaitUntilAsync(() => source.Info.Status == SourceStatus.Connected);
    }

    [Fact]
    public void Dispose_BeforeStart_DoesNotThrow()
    {
        using var source = new InputSource(4, SourceProtocol.Uvc, null, (_, _) => new FakeGstPipeline(), NullLogger.Instance);
    }

    [Fact]
    public async Task Dispose_WhileRunning_StopsThePipelineAndDoesNotReconnect()
    {
        var pipelines = new ConcurrentQueue<FakeGstPipeline>();
        IGstPipeline Factory(SourceProtocol protocol, string? url)
        {
            var pipeline = new FakeGstPipeline();
            pipelines.Enqueue(pipeline);
            return pipeline;
        }

        var source = new InputSource(5, SourceProtocol.Srt, null, Factory, NullLogger.Instance, FastInitialBackoff, FastMaxBackoff);
        source.Start();

        await WaitUntilAsync(() => pipelines.Count >= 1);

        source.Dispose();

        var countAfterDispose = pipelines.Count;
        await Task.Delay(TimeSpan.FromMilliseconds(150));

        Assert.Equal(countAfterDispose, pipelines.Count);
        Assert.All(pipelines, p => Assert.True(p.StopCount >= 1));
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        Assert.True(condition(), "Condition was not met within the timeout.");
    }
}
