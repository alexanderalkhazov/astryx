using Astryx.SharedKernel;

namespace Astryx.UnitTests.SharedKernel;

public class IdsTests
{
    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    [Fact]
    public void Ids_are_version_7() => Ids.New().Version.ShouldBe(7);

    [Fact]
    public void Ids_sort_by_creation_time()
    {
        var earlier = Ids.New(new FixedTime(new DateTimeOffset(2026, 11, 9, 19, 32, 0, TimeSpan.Zero)));
        var later = Ids.New(new FixedTime(new DateTimeOffset(2026, 11, 9, 19, 36, 8, TimeSpan.Zero)));

        string.CompareOrdinal(earlier.ToString(), later.ToString()).ShouldBeLessThan(0);
    }
}
