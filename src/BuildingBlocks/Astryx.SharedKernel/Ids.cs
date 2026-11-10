namespace Astryx.SharedKernel;

/// <summary>Time-ordered UUIDv7 identifiers (SYSTEM_DESIGN.md §10.1, ADR-013).</summary>
public static class Ids
{
    public static Guid New(TimeProvider timeProvider) => Guid.CreateVersion7(timeProvider.GetUtcNow());

    public static Guid New() => Guid.CreateVersion7();
}
