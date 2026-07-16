using NotificationService.Data;
using NotificationService.Tests.TestSupport;

namespace NotificationService.Tests.Data;

public sealed class SnowflakeIdGeneratorTests
{
    private static readonly DateTimeOffset Epoch = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void NextId_is_unique_and_strictly_increasing_when_many_ids_share_a_clock_tick()
    {
        var clock = new MutableTimeProvider(Epoch.AddMilliseconds(50));
        var generator = new SnowflakeIdGenerator(CreateOptions(), clock);

        // This crosses the 12-bit sequence boundary while the physical clock stays fixed.
        var ids = Enumerable.Range(0, 5_000).Select(_ => generator.NextId()).ToArray();

        Assert.Equal(ids.Length, ids.Distinct().Count());
        Assert.All(ids.Zip(ids.Skip(1)), pair => Assert.True(pair.Second > pair.First));
    }

    [Fact]
    public void NextId_remains_strictly_increasing_when_the_clock_moves_backwards()
    {
        var clock = new MutableTimeProvider(Epoch.AddSeconds(10));
        var generator = new SnowflakeIdGenerator(CreateOptions(), clock);

        var first = generator.NextId();
        clock.SetUtcNow(Epoch.AddSeconds(5));
        var afterRollback = generator.NextId();

        Assert.True(afterRollback > first);
    }

    private static SnowflakeOptions CreateOptions() => new()
    {
        NodeId = 42,
        Epoch = Epoch
    };
}
