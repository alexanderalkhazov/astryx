namespace Astryx.Modules.Strategies.Domain;

/// <summary>The outcome of one guard evaluation.</summary>
/// <param name="Drawdown">The last equity against the window's high, as a fraction (−0.054 = −5.4 %); 0 at a new high.</param>
public sealed record GuardEvaluation(decimal Drawdown, DateOnly AsOf, string Status);

/// <summary>
/// Strategy drawdown guard (SYSTEM_DESIGN.md §7.8): the drawdown of the strategy's live paper equity from its high over
/// a rolling window (calendar days, ending at the latest point). At <see cref="WarnFraction"/> of the pause threshold
/// the strategy is <c>active_guarded</c>; at the threshold it is <c>paused</c> and the scanner stops producing its
/// signals. A pause is never lifted automatically; a guarded strategy that recovers returns to <c>active</c>. Pure.
/// </summary>
public static class DrawdownGuard
{
    public const decimal DefaultPausePct = -0.0600m;
    public const int DefaultWindowDays = 30;
    public const decimal WarnFraction = 0.9m;

    /// <summary>Null when there are no points; <paramref name="points"/> may be in any order.</summary>
    public static GuardEvaluation? Evaluate(IReadOnlyCollection<(DateOnly D, decimal Equity)> points, string currentStatus, decimal pausePct = DefaultPausePct, int windowDays = DefaultWindowDays)
    {
        if (points.Count == 0)
        {
            return null;
        }

        var last = points.MaxBy(p => p.D);
        var from = last.D.AddDays(-(windowDays - 1));
        var high = points.Where(p => p.D >= from && p.D <= last.D).Max(p => p.Equity);
        var drawdown = high <= 0 ? 0m : Math.Round((last.Equity / high) - 1m, 6, MidpointRounding.ToEven);
        return new GuardEvaluation(drawdown, last.D, StatusFor(drawdown, currentStatus, pausePct));
    }

    public static decimal WarnAt(decimal pausePct) => pausePct * WarnFraction;

    private static string StatusFor(decimal drawdown, string currentStatus, decimal pausePct)
    {
        if (currentStatus is StrategyStatus.Paused or StrategyStatus.Retired)
        {
            return currentStatus; // resuming is a deliberate act, never automatic
        }

        if (drawdown <= pausePct)
        {
            return StrategyStatus.Paused;
        }

        return drawdown <= WarnAt(pausePct) ? StrategyStatus.ActiveGuarded : StrategyStatus.Active;
    }
}
