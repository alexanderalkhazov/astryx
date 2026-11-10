using Astryx.Infrastructure.Jobs;

namespace Astryx.UnitTests.Outbox;

public class JobSchedulingMathTests
{
    [Theory]
    [InlineData(1, 30)]
    [InlineData(2, 60)]
    [InlineData(5, 480)]
    [InlineData(6, 900)]   // capped at 15 min
    [InlineData(40, 900)]
    public void Job_backoff_doubles_and_caps(int attempts, int expectedSeconds) =>
        JobRunner.Backoff(attempts, new JobQueueOptions()).ShouldBe(TimeSpan.FromSeconds(expectedSeconds));

    [Fact]
    public void Slots_align_to_the_interval()
    {
        var now = new DateTimeOffset(2026, 11, 10, 14, 32, 47, TimeSpan.Zero);

        JobScheduler.Slot(now, TimeSpan.FromMinutes(1)).ShouldBe(new DateTimeOffset(2026, 11, 10, 14, 32, 0, TimeSpan.Zero));
        JobScheduler.Slot(now, TimeSpan.FromMinutes(15)).ShouldBe(new DateTimeOffset(2026, 11, 10, 14, 30, 0, TimeSpan.Zero));
    }
}
