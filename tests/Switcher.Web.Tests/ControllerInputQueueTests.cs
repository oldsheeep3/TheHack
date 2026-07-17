using Microsoft.Extensions.Logging.Abstractions;
using Switcher.Contracts;

namespace Switcher.Web.Tests;

public class ControllerInputQueueTests
{
    [Fact]
    public async Task Enqueue_FromMultipleConcurrentProducers_DeliversEveryEventSerially()
    {
        var sink = new RecordingControllerInputSink();
        await using var queue = new ControllerInputQueue(sink, NullLogger<ControllerInputQueue>.Instance);

        const int producers = 8;
        const int eventsPerProducer = 25;
        const int total = producers * eventsPerProducer;

        var producerTasks = Enumerable.Range(0, producers).Select(p => Task.Run(() =>
        {
            for (var i = 0; i < eventsPerProducer; i++)
            {
                var enqueued = queue.TryEnqueue(new ButtonEvent($"controller-{p}", i, Timestamp: i));
                Assert.True(enqueued);
            }
        }));

        await Task.WhenAll(producerTasks);

        await WaitUntilAsync(() => sink.Received.Count == total, TimeSpan.FromSeconds(5));

        Assert.Equal(total, sink.Received.Count);
        Assert.False(sink.ObservedOverlap, "IControllerInputSink.Enqueue must never be called concurrently.");
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }
    }
}
