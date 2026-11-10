namespace Astryx.Infrastructure.Outbox;

/// <summary>Ambient correlation ID for the current request or job, copied onto published outbox messages (§23.2).</summary>
public static class CorrelationContext
{
    private static readonly AsyncLocal<string?> Current = new();

    public static string? Id
    {
        get => Current.Value;
        set => Current.Value = value;
    }
}
