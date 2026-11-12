using Microsoft.Extensions.Options;

namespace Astryx.Modules.MarketData.Application.Ingestion;

/// <summary>
/// Token bucket holding at most one minute of requests (SYSTEM_DESIGN.md §16.3). Jobs stop and resume next cycle when
/// it runs dry rather than waiting, so a slow vendor never stalls the job queue.
/// </summary>
public sealed class RateBudget(IOptions<IngestionOptions> options, TimeProvider timeProvider)
{
    private readonly Lock _gate = new();
    private decimal _tokens = options.Value.RequestsPerMinute;
    private DateTimeOffset _refilledAt = timeProvider.GetUtcNow();

    private int Capacity => options.Value.RequestsPerMinute;

    public bool TryAcquire()
    {
        lock (_gate)
        {
            Refill();
            if (_tokens < 1)
            {
                return false;
            }

            _tokens--;
            return true;
        }
    }

    public int Available
    {
        get
        {
            lock (_gate)
            {
                Refill();
                return (int)_tokens;
            }
        }
    }

    private void Refill()
    {
        var now = timeProvider.GetUtcNow();
        var elapsed = (decimal)(now - _refilledAt).TotalSeconds;
        _tokens = Math.Min(Capacity, _tokens + (elapsed * Capacity / 60m));
        _refilledAt = now;
    }
}

/// <summary>Raised inside a sync when the budget is exhausted; the job records <c>rate_budget</c> and resumes next cycle.</summary>
public sealed class RateBudgetExhaustedException() : Exception("Market data request budget exhausted for this minute.");
