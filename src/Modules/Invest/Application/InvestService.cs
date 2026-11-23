using Astryx.Contracts.MarketData;
using Astryx.Infrastructure.Security;
using Astryx.Modules.Invest.Domain;
using Astryx.Modules.Invest.Infrastructure.Persistence;
using Astryx.SharedKernel;
using Astryx.SharedKernel.Finance;
using Astryx.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Astryx.Modules.Invest.Application;

public sealed record SaveInvestPlanRequest(string Profile, decimal? InitialDeposit, decimal ContributionAmount, string ContributionFrequency, decimal? GoalAmount, DateOnly? GoalDate);

public sealed record InvestFundDto(string Symbol, string Name, string Role, decimal TargetWeight, decimal Quantity, decimal? Price, decimal Value, decimal Weight, decimal CostBasis);

public sealed record InvestContributionDto(decimal Amount, string Frequency, DateOnly? NextOn);

public sealed record InvestGoalDto(decimal Amount, DateOnly? Date, decimal Progress);

public sealed record InvestTransactionDto(string Kind, string? Symbol, decimal? Quantity, decimal? Price, decimal Amount, DateTimeOffset OccurredAt);

/// <summary>I1: the plan valued at the latest prices, with its drift from target and the recent activity.</summary>
public sealed record InvestDto(
    string Profile, decimal Value, decimal Cash, decimal Contributed, decimal Gain, decimal GainPct, IReadOnlyList<InvestFundDto> Funds, decimal MaxDrift,
    bool RebalanceSuggested, InvestContributionDto Contribution, InvestGoalDto? Goal, IReadOnlyList<InvestTransactionDto> Activity, DateTimeOffset? PricesAsOf);

/// <summary>
/// The Invest track (TASKS.md T-164): a long-term paper portfolio of three broad index funds in a mix chosen from the
/// user's answers, bought at the latest stored prices (no slippage or fees, so it says "at the latest price"), with
/// recurring contributions and rebalancing. Separate from trading: no proposals, no risk engine, no broker.
/// </summary>
public sealed class InvestService(InvestDbContext db, ICurrentUser user, IStockResearchSource research, TimeProvider timeProvider)
{
    public static readonly Error NotFound = new("not_found", "You don't have an investing plan yet.");
    public static readonly Error PricesMissing = new("quote_stale", "Fund prices aren't available yet. Try again in a moment.");
    public static readonly Error NothingToInvest = new("conflict", "There is no cash waiting to be invested.");
    public const decimal DefaultInitialDeposit = 10_000m;

    public async Task<Result<InvestDto>> GetAsync(CancellationToken cancellationToken) =>
        await PlanAsync(user.RequireUserId(), cancellationToken) is { } plan ? await DtoAsync(plan, cancellationToken) : NotFound;

    /// <summary>I2: creates the plan (depositing and investing the initial amount) or changes its mix, contributions and goal.</summary>
    public async Task<Result<InvestDto>> SaveAsync(SaveInvestPlanRequest request, CancellationToken cancellationToken)
    {
        var userId = user.RequireUserId();
        var now = timeProvider.GetUtcNow();
        var plan = await PlanAsync(userId, cancellationToken);
        if (plan is null)
        {
            plan = new InvestPlan { Id = Ids.New(timeProvider), UserId = userId, Profile = request.Profile, CreatedAt = now };
            db.Plans.Add(plan);
            Deposit(plan, request.InitialDeposit ?? DefaultInitialDeposit, now);
        }

        plan.Profile = request.Profile;
        plan.ContributionAmount = request.ContributionFrequency == Frequencies.None ? 0m : RoundingPolicy.Ledger(request.ContributionAmount);
        if (plan.ContributionFrequency != request.ContributionFrequency || plan.NextContributionOn is null)
        {
            plan.NextContributionOn = Allocator.Next(request.ContributionFrequency, Today(now));
        }

        plan.ContributionFrequency = request.ContributionFrequency;
        (plan.GoalAmount, plan.GoalDate) = (request.GoalAmount, request.GoalDate);
        plan.UpdatedAt = now;
        plan.Version++;
        if (plan.Cash > 0m)
        {
            var invested = await InvestCashAsync(plan, now, cancellationToken);
            if (!invested.IsSuccess && invested.Error != NothingToInvest)
            {
                return invested.Error;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return await DtoAsync(plan, cancellationToken);
    }

    /// <summary>I3: spends idle cash in the target mix.</summary>
    public async Task<Result<InvestDto>> InvestNowAsync(CancellationToken cancellationToken)
    {
        if (await PlanAsync(user.RequireUserId(), cancellationToken) is not { } plan)
        {
            return NotFound;
        }

        var invested = await InvestCashAsync(plan, timeProvider.GetUtcNow(), cancellationToken);
        if (!invested.IsSuccess)
        {
            return invested.Error;
        }

        await db.SaveChangesAsync(cancellationToken);
        return await DtoAsync(plan, cancellationToken);
    }

    /// <summary>I4: brings every fund back to its target share.</summary>
    public async Task<Result<InvestDto>> RebalanceAsync(CancellationToken cancellationToken)
    {
        if (await PlanAsync(user.RequireUserId(), cancellationToken) is not { } plan)
        {
            return NotFound;
        }

        if (await PricesAsync(cancellationToken) is not { } prices)
        {
            return PricesMissing;
        }

        var holdings = await db.Holdings.Where(h => h.PlanId == plan.Id).ToListAsync(cancellationToken);
        var trades = Allocator.Rebalance(plan.Cash, holdings.ToDictionary(h => h.Symbol, h => h.Quantity), Mixes.Targets[plan.Profile], prices.Last);
        await ApplyAsync(plan, holdings, trades, timeProvider.GetUtcNow(), cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return await DtoAsync(plan, cancellationToken);
    }

    /// <summary>The recurring contribution for every plan that is due (run by <see cref="ContributionJob"/>). Returns how many ran.</summary>
    public async Task<int> ContributeDueAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var today = Today(now);
        var due = await db.Plans.IgnoreQueryFilters()
            .Where(p => p.ContributionFrequency != Frequencies.None && p.NextContributionOn <= today && p.ContributionAmount > 0m)
            .ToListAsync(cancellationToken);
        if (due.Count == 0 || await PricesAsync(cancellationToken) is null)
        {
            return 0; // no prices yet: they stay due and run next time
        }

        foreach (var plan in due)
        {
            Deposit(plan, plan.ContributionAmount, now);
            plan.NextContributionOn = Allocator.Next(plan.ContributionFrequency, plan.NextContributionOn!.Value);
            while (plan.NextContributionOn <= today)
            {
                plan.NextContributionOn = Allocator.Next(plan.ContributionFrequency, plan.NextContributionOn!.Value); // one deposit per run, never a backlog
            }

            await InvestCashAsync(plan, now, cancellationToken, ignoreFilters: true);
            plan.UpdatedAt = now;
            plan.Version++;
        }

        await db.SaveChangesAsync(cancellationToken);
        return due.Count;
    }

    private void Deposit(InvestPlan plan, decimal amount, DateTimeOffset now)
    {
        var rounded = RoundingPolicy.Ledger(amount);
        plan.Cash += rounded;
        plan.Contributed += rounded;
        db.Transactions.Add(new InvestTransaction { Id = Ids.New(timeProvider), UserId = plan.UserId, PlanId = plan.Id, Kind = "deposit", Amount = rounded, OccurredAt = now });
    }

    private async Task<Result<bool>> InvestCashAsync(InvestPlan plan, DateTimeOffset now, CancellationToken cancellationToken, bool ignoreFilters = false)
    {
        if (plan.Cash < 1m)
        {
            return NothingToInvest;
        }

        if (await PricesAsync(cancellationToken) is not { } prices)
        {
            return PricesMissing;
        }

        var holdings = await (ignoreFilters ? db.Holdings.IgnoreQueryFilters() : db.Holdings).Where(h => h.PlanId == plan.Id).ToListAsync(cancellationToken);
        await ApplyAsync(plan, holdings, Allocator.Invest(plan.Cash, Mixes.Targets[plan.Profile], prices.Last), now, cancellationToken);
        return true;
    }

    private Task ApplyAsync(InvestPlan plan, List<Holding> holdings, IReadOnlyList<FundTrade> trades, DateTimeOffset now, CancellationToken cancellationToken)
    {
        foreach (var trade in trades)
        {
            var holding = holdings.FirstOrDefault(h => h.Symbol == trade.Symbol);
            if (holding is null)
            {
                holding = new Holding { Id = Ids.New(timeProvider), UserId = plan.UserId, PlanId = plan.Id, Symbol = trade.Symbol };
                holdings.Add(holding);
                db.Holdings.Add(holding);
            }

            var amount = RoundingPolicy.Ledger(trade.Quantity * trade.Price);
            if (trade.Quantity > 0m)
            {
                holding.CostBasis += amount;
            }
            else if (holding.Quantity > 0m)
            {
                holding.CostBasis = RoundingPolicy.Ledger(holding.CostBasis * (holding.Quantity + trade.Quantity) / holding.Quantity); // average cost
            }

            holding.Quantity += trade.Quantity;
            plan.Cash = Math.Max(0m, plan.Cash - amount);
            db.Transactions.Add(new InvestTransaction
            {
                Id = Ids.New(timeProvider),
                UserId = plan.UserId,
                PlanId = plan.Id,
                Kind = trade.Quantity > 0m ? "buy" : "sell",
                Symbol = trade.Symbol,
                Quantity = Math.Abs(trade.Quantity),
                Price = trade.Price,
                Amount = -amount,
                OccurredAt = now,
            });
        }

        return Task.CompletedTask;
    }

    private sealed record Prices(IReadOnlyDictionary<string, decimal> Last, DateTimeOffset AsOf);

    /// <summary>The latest stored price of every fund, or null while any is missing.</summary>
    private async Task<Prices?> PricesAsync(CancellationToken cancellationToken)
    {
        var last = new Dictionary<string, decimal>(StringComparer.Ordinal);
        var asOf = DateTimeOffset.MaxValue;
        foreach (var fund in Mixes.Funds)
        {
            if (await research.GetQuoteAsync(fund.Symbol, cancellationToken) is not { Last: > 0m } quote)
            {
                return null;
            }

            last[fund.Symbol] = quote.Last;
            asOf = quote.AsOf < asOf ? quote.AsOf : asOf;
        }

        return new Prices(last, asOf);
    }

    private async Task<InvestPlan?> PlanAsync(Guid userId, CancellationToken cancellationToken) =>
        await db.Plans.SingleOrDefaultAsync(p => p.UserId == userId, cancellationToken);

    private async Task<InvestDto> DtoAsync(InvestPlan plan, CancellationToken cancellationToken)
    {
        var holdings = await db.Holdings.AsNoTracking().Where(h => h.PlanId == plan.Id).ToDictionaryAsync(h => h.Symbol, cancellationToken);
        var prices = await PricesAsync(cancellationToken);
        var last = prices?.Last ?? new Dictionary<string, decimal>();
        var quantities = holdings.ToDictionary(h => h.Key, h => h.Value.Quantity);
        var targets = Mixes.Targets[plan.Profile];
        var (weights, maxDrift) = Allocator.Drift(plan.Cash, quantities, targets, last);
        var value = plan.Cash + quantities.Sum(q => q.Value * last.GetValueOrDefault(q.Key));
        var funds = Mixes.Funds.Select(f =>
        {
            var quantity = quantities.GetValueOrDefault(f.Symbol);
            decimal? price = last.TryGetValue(f.Symbol, out var p) ? p : null;
            return new InvestFundDto(f.Symbol, f.Name, f.Role, targets[f.Symbol], quantity, price, RoundingPolicy.Ledger(quantity * (price ?? 0m)), Math.Round(weights[f.Symbol], 4), holdings.GetValueOrDefault(f.Symbol)?.CostBasis ?? 0m);
        }).ToList();
        var activity = await db.Transactions.AsNoTracking().Where(t => t.PlanId == plan.Id).OrderByDescending(t => t.OccurredAt).ThenBy(t => t.Kind).Take(20)
            .Select(t => new InvestTransactionDto(t.Kind, t.Symbol, t.Quantity, t.Price, t.Amount, t.OccurredAt)).ToListAsync(cancellationToken);
        var gain = RoundingPolicy.Ledger(value - plan.Contributed);
        return new InvestDto(
            plan.Profile, RoundingPolicy.Ledger(value), plan.Cash, plan.Contributed, gain, plan.Contributed == 0m ? 0m : Math.Round(gain / plan.Contributed, 4), funds,
            Math.Round(maxDrift, 4), maxDrift > Mixes.RebalanceDrift, new InvestContributionDto(plan.ContributionAmount, plan.ContributionFrequency, plan.NextContributionOn),
            plan.GoalAmount is { } goal and > 0m ? new InvestGoalDto(goal, plan.GoalDate, Math.Round(Math.Min(value / goal, 1m), 4)) : null,
            activity, prices?.AsOf);
    }

    /// <summary>Contributions follow New York calendar days.</summary>
    private static DateOnly Today(DateTimeOffset now) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, TimeZoneInfo.FindSystemTimeZoneById("America/New_York")).DateTime);
}
