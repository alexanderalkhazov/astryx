using Astryx.Infrastructure.Outbox;

namespace Astryx.UnitTests.Outbox;

public class OutboxBackoffTests
{
    private static readonly OutboxOptions Defaults = new();

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 4)]
    [InlineData(8, 128)]
    [InlineData(9, 256)]
    public void Backoff_doubles_from_the_base(int attempts, int expectedSeconds) =>
        OutboxDispatcher.Backoff(attempts, Defaults).ShouldBe(TimeSpan.FromSeconds(expectedSeconds));

    [Theory]
    [InlineData(10)]
    [InlineData(64)]
    public void Backoff_is_capped(int attempts) =>
        OutboxDispatcher.Backoff(attempts, Defaults).ShouldBe(TimeSpan.FromMinutes(5));
}
