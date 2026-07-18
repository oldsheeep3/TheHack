using System.Collections.Concurrent;
using Microsoft.Extensions.Logging.Abstractions;
using Switcher.Contracts;
using Switcher.Media.GStreamer;

namespace Switcher.Media.Tests;

public class InputSourceManagerTests
{
    [Fact]
    public void RemoveSource_WhenChannelDoesNotExist_IsANoOp()
    {
        using var manager = new InputSourceManager(NullLoggerFactory.Instance, (_, _) => new FakeGstPipeline());

        manager.RemoveSource(99);

        Assert.Empty(manager.GetSources());
    }

    [Fact]
    public async Task AddSource_CalledTwiceForTheSameChannel_ReplacesTheOldSourceIdempotently()
    {
        var pipelines = new ConcurrentQueue<FakeGstPipeline>();
        IGstPipeline Factory(SourceProtocol protocol, string? url)
        {
            var pipeline = new FakeGstPipeline();
            pipelines.Enqueue(pipeline);
            return pipeline;
        }

        using var manager = new InputSourceManager(NullLoggerFactory.Instance, Factory);

        manager.AddSource(1, SourceProtocol.Uvc, null);
        await WaitUntilAsync(() => pipelines.Count >= 1);
        pipelines.TryPeek(out var first);

        manager.AddSource(1, SourceProtocol.Ndi, "replacement");
        await WaitUntilAsync(() => pipelines.Count >= 2);
        await WaitUntilAsync(() => first!.StopCount >= 1);

        var sources = manager.GetSources();
        var source = Assert.Single(sources);
        Assert.Equal(1, source.Channel);
        Assert.Equal(SourceProtocol.Ndi, source.Protocol);
    }

    [Fact]
    public void GetSources_ReturnsASnapshotOrderedByChannel()
    {
        using var manager = new InputSourceManager(NullLoggerFactory.Instance, (_, _) => new FakeGstPipeline());

        manager.AddSource(3, SourceProtocol.Srt, null);
        manager.AddSource(1, SourceProtocol.Uvc, null);
        manager.AddSource(2, SourceProtocol.Ndi, null);

        Assert.Equal([1, 2, 3], manager.GetSources().Select(s => s.Channel));
    }

    [Fact]
    public async Task OneChannelFaulting_DoesNotAffectTheStatusOfOtherChannels()
    {
        var pipelinesByProtocol = new ConcurrentDictionary<SourceProtocol, FakeGstPipeline>();
        IGstPipeline Factory(SourceProtocol protocol, string? url) =>
            pipelinesByProtocol.GetOrAdd(protocol, _ => new FakeGstPipeline());

        using var manager = new InputSourceManager(NullLoggerFactory.Instance, Factory);
        manager.AddSource(1, SourceProtocol.Uvc, null);
        manager.AddSource(2, SourceProtocol.Ndi, null);

        await WaitUntilAsync(() => manager.GetSources().All(s => s.Status == SourceStatus.Connected));

        pipelinesByProtocol[SourceProtocol.Uvc].RaiseFaulted("simulated failure on channel 1");

        await WaitUntilAsync(() => manager.GetSources().Single(s => s.Channel == 1).Status == SourceStatus.Error);
        Assert.Equal(SourceStatus.Connected, manager.GetSources().Single(s => s.Channel == 2).Status);
    }

    [Fact]
    public async Task SourceStatusChanged_ForwardsUpdatesFromInputSources()
    {
        using var manager = new InputSourceManager(NullLoggerFactory.Instance, (_, _) => new FakeGstPipeline());
        var received = new ConcurrentQueue<SourceInfo>();
        manager.SourceStatusChanged += (_, info) => received.Enqueue(info);

        manager.AddSource(7, SourceProtocol.Srt, null);

        await WaitUntilAsync(() => received.Any(info => info is { Channel: 7, Status: SourceStatus.Connected }));
    }

    [Fact]
    public async Task TryGetLatestFrame_ReflectsTheMostRecentlyDecodedFrame()
    {
        var pipelines = new ConcurrentQueue<FakeGstPipeline>();
        IGstPipeline Factory(SourceProtocol protocol, string? url)
        {
            var pipeline = new FakeGstPipeline();
            pipelines.Enqueue(pipeline);
            return pipeline;
        }

        using var manager = new InputSourceManager(NullLoggerFactory.Instance, Factory);
        manager.AddSource(1, SourceProtocol.Srt, null);

        await WaitUntilAsync(() => pipelines.Count >= 1);
        Assert.False(manager.TryGetLatestFrame(1, out _));
        Assert.False(manager.TryGetLatestFrame(404, out _));

        var frame = new FrameData(320, 240, new byte[4]);
        pipelines.TryPeek(out var pipeline);
        pipeline!.RaiseFrame(frame);

        await WaitUntilAsync(() => manager.TryGetLatestFrame(1, out var latest) && latest == frame);
    }

    [Fact]
    public void AddSource_ById_AssignsAStableOrdinalAndIsListedById()
    {
        using var manager = new InputSourceManager(NullLoggerFactory.Instance, (_, _) => new FakeGstPipeline());

        var info = manager.AddSource(new SourceDefinition("src-a", "Cam A", SourceType.Ndi, new NdiConfig("CAM-A"), null, null));

        Assert.Equal("src-a", info.Id);
        Assert.Equal("Cam A", info.Name);

        var listed = Assert.Single(manager.GetSources());
        Assert.Equal("src-a", listed.Id);
        Assert.Equal(info.Channel, listed.Channel);
    }

    [Fact]
    public void AddSource_ById_CalledTwiceForTheSameId_ReplacesInPlaceKeepingTheSameChannel()
    {
        using var manager = new InputSourceManager(NullLoggerFactory.Instance, (_, _) => new FakeGstPipeline());

        var first = manager.AddSource(new SourceDefinition("src-a", "Cam A", SourceType.Ndi, new NdiConfig("CAM-A"), null, null));
        var second = manager.AddSource(new SourceDefinition("src-a", "Cam A2", SourceType.Ndi, new NdiConfig("CAM-A2"), null, null));

        Assert.Equal(first.Channel, second.Channel);
        var listed = Assert.Single(manager.GetSources());
        Assert.Equal("Cam A2", listed.Name);
    }

    [Fact]
    public void AddSource_ById_DoesNotCollideWithChannelsUsedByOtherIds()
    {
        using var manager = new InputSourceManager(NullLoggerFactory.Instance, (_, _) => new FakeGstPipeline());

        var a = manager.AddSource(new SourceDefinition("src-a", "A", SourceType.Ndi, new NdiConfig("A"), null, null));
        var b = manager.AddSource(new SourceDefinition("src-b", "B", SourceType.Ndi, new NdiConfig("B"), null, null));

        Assert.NotEqual(a.Channel, b.Channel);
        Assert.Equal(2, manager.GetSources().Count);
    }

    [Fact]
    public void RemoveSource_ById_RemovesTheSourceAndFreesItsChannel()
    {
        using var manager = new InputSourceManager(NullLoggerFactory.Instance, (_, _) => new FakeGstPipeline());
        manager.AddSource(new SourceDefinition("src-a", "A", SourceType.Ndi, new NdiConfig("A"), null, null));

        manager.RemoveSource("src-a");

        Assert.Empty(manager.GetSources());
    }

    [Fact]
    public void RemoveSource_ById_WhenIdDoesNotExist_IsANoOp()
    {
        using var manager = new InputSourceManager(NullLoggerFactory.Instance, (_, _) => new FakeGstPipeline());

        manager.RemoveSource("does-not-exist");

        Assert.Empty(manager.GetSources());
    }

    [Fact]
    public void Duplicate_ClonesTheSourceUnderANewGeneratedIdAndChannel()
    {
        using var manager = new InputSourceManager(NullLoggerFactory.Instance, (_, _) => new FakeGstPipeline());
        var original = manager.AddSource(new SourceDefinition("src-a", "Cam A", SourceType.Ndi, new NdiConfig("CAM-A"), null, null));

        var copy = manager.Duplicate("src-a");

        Assert.NotEqual(original.Id, copy.Id);
        Assert.NotEqual(original.Channel, copy.Channel);
        Assert.Equal(2, manager.GetSources().Count);
    }

    [Fact]
    public void Duplicate_UnknownId_Throws()
    {
        using var manager = new InputSourceManager(NullLoggerFactory.Instance, (_, _) => new FakeGstPipeline());

        Assert.Throws<ArgumentException>(() => manager.Duplicate("does-not-exist"));
    }

    [Fact]
    public void Reorder_ChangesListOrderWithoutChangingChannels()
    {
        using var manager = new InputSourceManager(NullLoggerFactory.Instance, (_, _) => new FakeGstPipeline());
        var a = manager.AddSource(new SourceDefinition("src-a", "A", SourceType.Ndi, new NdiConfig("A"), null, null));
        var b = manager.AddSource(new SourceDefinition("src-b", "B", SourceType.Ndi, new NdiConfig("B"), null, null));

        manager.Reorder(["src-b", "src-a"]);

        Assert.Equal(["src-b", "src-a"], manager.GetSources().Select(s => s.Id));
        Assert.Equal(a.Channel, manager.GetSources().Single(s => s.Id == "src-a").Channel);
        Assert.Equal(b.Channel, manager.GetSources().Single(s => s.Id == "src-b").Channel);
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
