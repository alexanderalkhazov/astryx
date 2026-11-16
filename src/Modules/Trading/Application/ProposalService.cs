using System.Globalization;
using System.Text;
using System.Text.Json;
using Astryx.Contracts.Ai;
using Astryx.Contracts.Discovery;
using Astryx.Contracts.MarketData;
using Astryx.Contracts.Portfolio;
using Astryx.Infrastructure.Security;
using Astryx.Modules.Trading.Domain;
using Astryx.Modules.Trading.Infrastructure.Persistence;
using Astryx.SharedKernel;
using Astryx.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Astryx.Modules.Trading.Application;

/// <summary>
/// T1–T3 and T5 (SYSTEM_DESIGN.md §7.9, §11.7). A proposal is created from an active signal (its deterministic levels)
/// or from the user's validated analysis (only when LevelValidator accepted the levels), on fresh data, inside the
/// regular session. One pending proposal per instrument; it expires 30 minutes before the close.
/// </summary>
public sealed class ProposalService(
    TradingDbContext db,
    ICurrentUser user,
    ISignalLookup signals,
    IAnalysisLookup analyses,
    IInstrumentDirectory instruments,
    IStockResearchSource research,
    IPortfolioDirectory portfolios,
    IMarketSessionClock sessions,
    AssessmentService assessments)
{
    public const string Label = "RESEARCH PROPOSAL · NOT EXECUTED";
    public const int MaxLimit = 50;

    /// <summary>Proposals stop being approvable this long before the regular close (§7.9).</summary>
    public static readonly TimeSpan CloseBuffer = TimeSpan.FromMinutes(30);

    public static readonly Error NotFound = new("not_found", "No proposal with that id.");
    public static readonly Error SignalNotFound = new("not_found", "No signal with that id.");
    public static readonly Error AnalysisNotFound = new("not_found", "No analysis with that id.");
    public static readonly Error SignalNotActive = new("signal_not_active", "This signal is no longer active, so it can't seed a proposal.");
    public static readonly Error AnalysisStale = new("analysis_stale", "This analysis is stale. Run a new one to propose a trade from it.");
    public static readonly Error LevelsRejected = new("ai_levels_rejected", "The analysis levels failed the deterministic checks, so they can't seed a proposal.");
    public static readonly Error QuoteStale = new("quote_stale", "The quote is stale. Proposals need live data.");
    public static readonly Error MarketClosed = new("market_closed", "Proposals can be created during the regular session, until 30 minutes before the close.");
    public static readonly Error NoPortfolio = new("conflict", "Your paper portfolio isn't ready yet. Try again in a moment.");
    public static readonly Error NotPending = new("proposal_not_pending", "This proposal is no longer pending.");
    public static readonly Error BadSource = new("validation_failed", "Use {source:\"signal\", signalId} or {source:\"analysis\", analysisId}.");
    public static readonly Error BadStatus = new("bad_request", "status must be pending, approved, rejected or expired.");
    public static readonly Error BadCursor = new("bad_request", "cursor is not valid.");
    public static readonly Error LevelsInvalid = new("validation_failed", "Levels must satisfy stop < entry < target for a long.");

    private static readonly string[] Statuses = [ProposalStatus.Pending, ProposalStatus.Approved, ProposalStatus.Rejected, ProposalStatus.Expired];

    public async Task<Result<CreateProposalOutcome>> CreateAsync(CreateProposalRequest request, CancellationToken cancellationToken)
    {
        var userId = user.RequireUserId();
        Proposal draft;
        switch (request)
        {
            case { Source: ProposalSource.Signal, SignalId: { } signalId, AnalysisId: null }:
                {
                    if (await signals.GetAsync(signalId, cancellationToken) is not { } signal)
                    {
                        return SignalNotFound;
                    }

                    if (signal.Status != "active")
                    {
                        return SignalNotActive;
                    }

                    var (headline, summary, evidence, invalidation) = ProposalCopy.FromSignal(signal);
                    draft = Draft(userId, signal.InstrumentId, signal.Symbol, ProposalSource.Signal, signal.OrderType, signal.Entry, signal.Stop, signal.Target, headline, summary, evidence, invalidation, "template");
                    draft.SignalId = signal.Id;
                    draft.StrategyId = signal.StrategyId;
                    draft.StrategyVersionId = signal.StrategyVersionId;
                    draft.StrategyName = signal.StrategyName;
                    draft.SourceAt = signal.EvaluatedAt;
                    draft.Score = signal.Score;
                    break;
                }

            case { Source: ProposalSource.Analysis, AnalysisId: { } analysisId, SignalId: null }:
                {
                    if (await analyses.GetAsync(userId, analysisId, cancellationToken) is not { } analysis)
                    {
                        return AnalysisNotFound;
                    }

                    if (analysis.Stale)
                    {
                        return AnalysisStale;
                    }

                    if (!analysis.LevelsAccepted || analysis.EntryHigh is not { } entry || analysis.Stop is not { } stop || analysis.Target is not { } target)
                    {
                        return LevelsRejected;
                    }

                    var quote = await research.GetQuoteAsync(analysis.Symbol, cancellationToken);
                    // The top of the entry zone, the worst fill for a long (as LevelValidator assumed): a buy stop above the market, else a limit.
                    var orderType = quote is not null && entry > quote.Last ? "buy_stop" : "limit";
                    var (headline, summary, evidence, invalidation) = ProposalCopy.FromAnalysis(analysis);
                    draft = Draft(userId, analysis.InstrumentId, analysis.Symbol, ProposalSource.Analysis, orderType, entry, stop, target, headline, summary, evidence, invalidation, "ai");
                    draft.AnalysisResultId = analysis.Id;
                    draft.SourceAt = analysis.AnalysisAt;
                    break;
                }

            default:
                return BadSource;
        }

        if (!(draft.StopPrice < draft.EntryPrice && draft.EntryPrice < draft.TargetPrice))
        {
            return LevelsInvalid; // RISK-05: never reaches the table's check constraint
        }

        if (await db.Proposals.AsNoTracking().FirstOrDefaultAsync(p => p.InstrumentId == draft.InstrumentId && p.Status == ProposalStatus.Pending, cancellationToken) is { } existing)
        {
            // The scanner re-issues signals every run: a pending proposal whose signal is no longer active can never be
            // approved (data_fresh fails), so it expires and the current signal gets a fresh proposal instead.
            if (existing.SignalId is not { } previousSignal || previousSignal == draft.SignalId
                || (await signals.GetAsync(previousSignal, cancellationToken))?.Status == "active")
            {
                return new CreateProposalOutcome(await ToDtoAsync(existing, cancellationToken), Created: false);
            }

            await db.Proposals.Where(p => p.Id == existing.Id && p.Status == ProposalStatus.Pending).ExecuteUpdateAsync(
                u => u.SetProperty(p => p.Status, ProposalStatus.Expired).SetProperty(p => p.DecidedAt, sessions.Now).SetProperty(p => p.Version, p => p.Version + 1), cancellationToken);
        }

        if (await research.GetQuoteAsync(draft.Symbol, cancellationToken) is not { Stale: false })
        {
            return QuoteStale;
        }

        var now = sessions.Now;
        var session = await sessions.TodayAsync(cancellationToken);
        if (session is null || now < session.OpenAt || now >= session.CloseAt - CloseBuffer)
        {
            return MarketClosed;
        }

        if (await portfolios.GetSimulatedAsync(userId, cancellationToken) is not { } portfolioId)
        {
            return NoPortfolio;
        }

        draft.PortfolioId = portfolioId;
        draft.CreatedAt = now;
        draft.ExpiresAt = session.CloseAt - CloseBuffer;
        db.Proposals.Add(draft);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Another request created the instrument's pending proposal first: answer with that one.
            db.Entry(draft).State = EntityState.Detached;
            var raced = await db.Proposals.AsNoTracking().FirstAsync(p => p.InstrumentId == draft.InstrumentId && p.Status == ProposalStatus.Pending, cancellationToken);
            return new CreateProposalOutcome(await ToDtoAsync(raced, cancellationToken), Created: false);
        }

        // The initial default-sized assessment (§14.1 step 1). A pending proposal answers 201 even if it fails checks.
        await assessments.CreateAsync(draft, quantity: null, riskPerTradePct: null, cancellationToken);
        return new CreateProposalOutcome(await ToDtoAsync(draft, cancellationToken), Created: true);
    }

    public async Task<Result<ProposalDto>> GetAsync(Guid id, CancellationToken cancellationToken) =>
        await db.Proposals.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, cancellationToken) is { } p ? await ToDtoAsync(p, cancellationToken) : NotFound;

    public async Task<Result<ProposalPageDto>> ListAsync(string? status, int? limit, string? cursor, CancellationToken cancellationToken)
    {
        if (status is not null && !Statuses.Contains(status))
        {
            return BadStatus;
        }

        var query = db.Proposals.AsNoTracking();
        if (status is not null)
        {
            query = query.Where(p => p.Status == status);
        }

        if (cursor is not null)
        {
            if (DecodeCursor(cursor) is not { } after)
            {
                return BadCursor;
            }

            query = query.Where(p => p.CreatedAt < after.At || (p.CreatedAt == after.At && p.Id.CompareTo(after.Id) < 0));
        }

        var take = Math.Clamp(limit ?? 20, 1, MaxLimit);
        var rows = await query.OrderByDescending(p => p.CreatedAt).ThenByDescending(p => p.Id).Take(take + 1).ToListAsync(cancellationToken);
        var names = await NamesAsync(rows.Take(take).Select(r => r.InstrumentId), cancellationToken);
        var items = rows.Take(take).Select(r => ToDto(r, names.GetValueOrDefault(r.InstrumentId, r.Symbol))).ToList();
        return new ProposalPageDto(items, rows.Count > take ? EncodeCursor(rows[take - 1]) : null);
    }

    /// <summary>T5. The signal is left untouched, so hit-rate audits still see it.</summary>
    public async Task<Result<ProposalDto>> RejectAsync(Guid id, string? reason, CancellationToken cancellationToken)
    {
        if (await db.Proposals.FirstOrDefaultAsync(p => p.Id == id, cancellationToken) is not { } proposal)
        {
            return NotFound;
        }

        var now = sessions.Now;
        if (proposal.Status != ProposalStatus.Pending || proposal.ExpiresAt <= now)
        {
            return NotPending;
        }

        proposal.Status = ProposalStatus.Rejected;
        proposal.RejectReason = "user_rejected";
        proposal.RejectNote = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        proposal.DecidedAt = now;
        proposal.Version++;
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return NotPending; // decided concurrently (approved, expired or rejected)
        }

        return await ToDtoAsync(proposal, cancellationToken);
    }

    private static Proposal Draft(
        Guid userId, Guid instrumentId, string symbol, string source, string orderType, decimal entry, decimal stop, decimal target,
        string headline, string summary, IReadOnlyList<string> evidence, IReadOnlyList<string> invalidation, string copySource) =>
        new()
        {
            Id = Ids.New(),
            UserId = userId,
            InstrumentId = instrumentId,
            Symbol = symbol,
            Source = source,
            OrderType = orderType,
            EntryPrice = entry,
            StopPrice = stop,
            TargetPrice = target,
            Headline = headline,
            Summary = summary,
            Evidence = JsonSerializer.Serialize(evidence),
            Invalidation = JsonSerializer.Serialize(invalidation),
            CopySource = copySource,
        };

    private async Task<ProposalDto> ToDtoAsync(Proposal p, CancellationToken cancellationToken) =>
        ToDto(p, (await NamesAsync([p.InstrumentId], cancellationToken)).GetValueOrDefault(p.InstrumentId, p.Symbol))
            with
        { Assessment = await assessments.LatestAsync(p, cancellationToken) };

    private async Task<Dictionary<Guid, string>> NamesAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken) =>
        (await instruments.GetAsync(ids.Distinct().ToList(), cancellationToken)).ToDictionary(i => i.Id, i => i.Name);

    private static ProposalDto ToDto(Proposal p, string name) => new(
        p.Id, p.Status, Label, p.Symbol, name, p.Side, "long", p.OrderType, p.TimeInForce,
        Round(p.EntryPrice), Round(p.StopPrice), Round(p.TargetPrice),
        Math.Round((p.StopPrice / p.EntryPrice) - 1, 4, MidpointRounding.ToEven), Math.Round((p.TargetPrice / p.EntryPrice) - 1, 4, MidpointRounding.ToEven),
        new ProposalSourceDto(p.Source, p.SignalId, p.AnalysisResultId, p.StrategyId, p.StrategyName, p.SourceAt),
        new ProposalAnalysisDto(
            p.Headline, p.Summary, JsonSerializer.Deserialize<List<string>>(p.Evidence)!, JsonSerializer.Deserialize<List<string>>(p.Invalidation)!,
            p.Score is { } s ? Math.Round(s, 0, MidpointRounding.ToEven).ToString(CultureInfo.InvariantCulture) : null, p.CopySource),
        p.CreatedAt, p.ExpiresAt, p.DecidedAt, p.RejectReason, Assessment: null);

    private static decimal Round(decimal price) => Math.Round(price, Math.Abs(price) >= 1 ? 2 : 4, MidpointRounding.ToEven);

    private static string EncodeCursor(Proposal last) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes($"{last.CreatedAt.UtcTicks.ToString(CultureInfo.InvariantCulture)}|{last.Id:N}"));

    private static (DateTimeOffset At, Guid Id)? DecodeCursor(string cursor)
    {
        try
        {
            var parts = Encoding.UTF8.GetString(Convert.FromBase64String(cursor)).Split('|');
            return parts.Length == 2 && long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks) && Guid.TryParseExact(parts[1], "N", out var id)
                ? (new DateTimeOffset(ticks, TimeSpan.Zero), id)
                : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
