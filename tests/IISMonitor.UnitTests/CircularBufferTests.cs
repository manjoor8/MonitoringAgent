using IISMonitor.Core.Collections;
using Xunit;

namespace IISMonitor.UnitTests;

public class CircularBufferTests
{
    [Fact]
    public void PushAndCount_WithinCapacity()
    {
        var buffer = new CircularBuffer<int>(5);
        buffer.Push(10);
        buffer.Push(20);
        buffer.Push(30);

        Assert.Equal(3, buffer.Count);
        Assert.Equal(5, buffer.Capacity);
    }

    [Fact]
    public void Push_OverCapacity_OverwritesOldest()
    {
        var buffer = new CircularBuffer<int>(3);
        buffer.Push(1);
        buffer.Push(2);
        buffer.Push(3);
        buffer.Push(4); // Overwrites 1

        Assert.Equal(3, buffer.Count);
        var snapshot = buffer.Snapshot();
        Assert.Equal(new[] { 2, 3, 4 }, snapshot);
    }

    [Fact]
    public void Snapshot_PreservesChronologicalOrderAcrossWraparound()
    {
        var buffer = new CircularBuffer<int>(4);
        for (int i = 1; i <= 10; i++)
        {
            buffer.Push(i);
        }

        Assert.Equal(4, buffer.Count);
        var snapshot = buffer.Snapshot();
        Assert.Equal(new[] { 7, 8, 9, 10 }, snapshot);
    }

    [Fact]
    public void TakeLatest_ReturnsRequestedCount()
    {
        var buffer = new CircularBuffer<int>(5);
        for (int i = 1; i <= 5; i++) buffer.Push(i);

        var latest = buffer.TakeLatest(2);
        Assert.Equal(new[] { 4, 5 }, latest);
    }

    [Fact]
    public void Clear_EmptiesBuffer()
    {
        var buffer = new CircularBuffer<int>(5);
        buffer.Push(1);
        buffer.Push(2);
        buffer.Clear();

        Assert.Equal(0, buffer.Count);
        Assert.Empty(buffer.Snapshot());
    }

    [Fact]
    public void ThreadSafety_ConcurrentPushesAndSnapshots()
    {
        var buffer = new CircularBuffer<int>(100);
        const int iterations = 1000;

        Parallel.Invoke(
            () =>
            {
                for (int i = 0; i < iterations; i++) buffer.Push(i);
            },
            () =>
            {
                for (int i = 0; i < iterations; i++)
                {
                    var snap = buffer.Snapshot();
                    Assert.True(snap.Length <= 100);
                }
            }
        );

        Assert.Equal(100, buffer.Count);
    }
}
