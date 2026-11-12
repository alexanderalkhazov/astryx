using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Astryx.Contracts.MarketData;
using Astryx.Infrastructure.Security;
using Astryx.Modules.Ai.Domain;
using Astryx.Modules.Ai.Infrastructure.Persistence;
using Astryx.SharedKernel;
using Astryx.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Astryx.Modules.Ai.Application.Jobs;

/// <summary>AI1 outcome: a new job (202), an existing equivalent one (200), or the id of the active job blocking it (409).</summary>
public sealed record CreateAnalysisOutcome(AnalysisJobDto? Job, bool Created, Guid? ActiveJobId);

/// <summary>
/// AI1–AI5 (SYSTEM_DESIGN.md §11.4, §12.8). All reads go through the user-scoped context, so another user's job or
/// result is a 404. Invalid outputs are stored for evaluation but never served.
/// </summary>
public sealed class AnalysisJobService(AiDbContext db, ICurrentUser user, IInstrumentDirectory instruments, IStockResearchSource research, IOptions<AnalysisJobOptions> options, TimeProvider timeProvider)
{
    public static readonly Error NotFound = new("not_found", "No analysis with that id.");
    public static readonly Error UnsupportedKind = new("bad_request", "kind must be stock_analysis (signal_explanation isn't available yet).");
    public static readonly Error UnsupportedTimeframe = new("bad_request", "Only timeframe=1d is supported.");
    public static readonly Error QuotaExceeded = new("ai_quota_exceeded", "You've used today's AI analyses. Indicators are still available.");
    public static readonly Error AlreadyFinished = new("conflict", "The analysis has already finished.");
    public static readonly Error BadCursor = new("bad_request", "cursor is not valid.");

    public const int MaxLimit = 50;

    /// <summary>Results this old are stale even if their data was fresh (§7.7).</summary>
    public static readonly TimeSpan ResultLifetime = TimeSpan.FromHours(24);

    public async Task<Result<CreateAnalysisOutcome>> CreateAsync(CreateAnalysisRequest request, string idempotencyKey, CancellationToken cancellationToken)
    {
        var userId = user.RequireUserId();
        var kind = request.Kind ?? AnalysisKind.StockAnalysis;
        if (kind != AnalysisKind.StockAnalysis)
        {
            return UnsupportedKind;
        }

        var timeframe = (request.Timeframe ?? "1d").ToLowerInvariant();
        if (timeframe != "1d")
        {
            return UnsupportedTimeframe;
        }

        if (await instruments.FindAsync(request.Symbol, cancellationToken) is not { } instrument)
        {
            return new Error("instrument_not_found", "No instrument with that symbol.");
        }

        if (await db.Jobs.AsNoTracking().FirstOrDefaultAsync(j => j.IdempotencyKey == idempotencyKey, cancellationToken) is { } replay)
        {
            return new CreateAnalysisOutcome(ToDto(replay), Created: false, null);
        }

        var now = timeProvider.GetUtcNow();
        var settings = options.Value;
        if (await db.Jobs.CountAsync(j => j.CreatedAt > now.AddDays(-1), cancellationToken) >= settings.DailyQuota)
        {
            return QuotaExceeded;
        }

        var quote = await research.GetQuoteAsync(instrument.Symbol, cancellationToken);
        var hash = RequestHash(instrument.Id, kind, timeframe, quote?.AsOf);
        var recent = now.AddMinutes(-settings.DedupeMinutes);
        var equivalent = await db.Jobs.AsNoTracking()
            .Where(j => j.RequestHash == hash && (j.Status == AnalysisJobStatus.Pending || j.Status == AnalysisJobStatus.Processing || (j.Status == AnalysisJobStatus.Completed && j.CompletedAt > recent)))
            .OrderByDescending(j => j.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (equivalent is not null)
        {
            return new CreateAnalysisOutcome(ToDto(equivalent), Created: false, null);
        }

        if (await ActiveJobIdAsync(instrument.Id, kind, cancellationToken) is { } active)
        {
            return new CreateAnalysisOutcome(null, Created: false, active);
        }

        var job = new AnalysisJob
        {
            Id = Ids.New(timeProvider),
            UserId = userId,
            InstrumentId = instrument.Id,
            Symbol = instrument.Symbol,
            SignalId = request.SignalId,
            Kind = kind,
            Timeframe = timeframe,
            IdempotencyKey = idempotencyKey,
            RequestHash = hash,
            MaxAttempts = settings.MaxAttempts,
            RunAfter = now,
            CreatedAt = now,
        };
        db.Jobs.Add(job);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // A concurrent request won the race: same key, or another active job for this symbol.
            db.Entry(job).State = EntityState.Detached;
            if (await db.Jobs.AsNoTracking().FirstOrDefaultAsync(j => j.IdempotencyKey == idempotencyKey, cancellationToken) is { } raced)
            {
                return new CreateAnalysisOutcome(ToDto(raced), Created: false, null);
            }

            return new CreateAnalysisOutcome(null, Created: false, await ActiveJobIdAsync(instrument.Id, kind, cancellationToken));
        }

        return new CreateAnalysisOutcome(ToDto(job), Created: true, null);
    }

    public async Task<Result<AnalysisJobDto>> GetJobAsync(Guid id, CancellationToken cancellationToken) =>
        await db.Jobs.AsNoTracking().FirstOrDefaultAsync(j => j.Id == id, cancellationToken) is { } job ? ToDto(job) : NotFound;

    /// <summary>AI3: pending jobs are cancelled now; a processing job stops at the worker's next checkpoint.</summary>
    public async Task<Result<AnalysisJobDto>> CancelAsync(Guid id, CancellationToken cancellationToken)
    {
        if (await db.Jobs.FirstOrDefaultAsync(j => j.Id == id, cancellationToken) is not { } job)
        {
            return NotFound;
        }

        if (AnalysisJobStatus.IsTerminal(job.Status))
        {
            return AlreadyFinished;
        }

        var now = timeProvider.GetUtcNow();
        var pending = await db.Jobs.Where(j => j.Id == id && j.Status == AnalysisJobStatus.Pending).ExecuteUpdateAsync(
            s => s.SetProperty(j => j.Status, AnalysisJobStatus.Cancelled).SetProperty(j => j.CompletedAt, now)
                .SetProperty(j => j.ErrorCode, AnalysisErrorCodes.Cancelled).SetProperty(j => j.ErrorDetail, "Cancelled.").SetProperty(j => j.CancelRequestedAt, now),
            cancellationToken);
        if (pending == 0)
        {
            await db.Jobs.Where(j => j.Id == id && j.Status == AnalysisJobStatus.Processing).ExecuteUpdateAsync(s => s.SetProperty(j => j.CancelRequestedAt, now), cancellationToken);
        }
        else
        {
            db.Entry(job).State = EntityState.Detached;
            db.Publish(AnalysisJobProcessor.Updated(job, AnalysisJobStatus.Cancelled, null, AnalysisErrorCodes.Cancelled));
            await db.SaveChangesAsync(cancellationToken);
        }

        return ToDto(await db.Jobs.AsNoTracking().SingleAsync(j => j.Id == id, cancellationToken));
    }

    public async Task<Result<AnalysisResultDto>> GetResultAsync(Guid id, CancellationToken cancellationToken)
    {
        var result = await db.Results.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id && r.ValidationStatus != ValidationStatusNames.Invalid, cancellationToken);
        return result is null ? NotFound : ToDto(result, timeProvider.GetUtcNow());
    }

    public async Task<Result<AnalysisPageDto>> ListAsync(string? symbol, int? limit, string? cursor, CancellationToken cancellationToken)
    {
        var take = Math.Clamp(limit ?? 20, 1, MaxLimit);
        var query = db.Results.AsNoTracking().Where(r => r.ValidationStatus != ValidationStatusNames.Invalid);
        if (!string.IsNullOrWhiteSpace(symbol))
        {
            var normalized = symbol.Trim().ToUpperInvariant();
            query = query.Where(r => r.Symbol == normalized);
        }

        if (cursor is not null)
        {
            if (DecodeCursor(cursor) is not { } after)
            {
                return BadCursor;
            }

            query = query.Where(r => r.AnalysisAt < after.At || (r.AnalysisAt == after.At && r.Id.CompareTo(after.Id) < 0));
        }

        var rows = await query.OrderByDescending(r => r.AnalysisAt).ThenByDescending(r => r.Id).Take(take + 1).ToListAsync(cancellationToken);
        var now = timeProvider.GetUtcNow();
        var items = rows.Take(take).Select(r => new AnalysisSummaryDto(
            r.Id, r.JobId, r.Symbol, r.Sentiment, r.Confidence, r.AnalysisAt, r.DataAsOf, IsStale(r, now), LevelsAccepted(r), r.RewardRisk)).ToList();
        var next = rows.Count > take ? EncodeCursor(rows[take - 1]) : null;
        return new AnalysisPageDto(items, next);
    }

    /// <summary>Same instrument, kind, timeframe and 15-minute bucket of the latest quote: the same question.</summary>
    public static string RequestHash(Guid instrumentId, string kind, string timeframe, DateTimeOffset? dataAsOf)
    {
        var bucket = dataAsOf is { } at ? new DateTimeOffset(at.UtcTicks - (at.UtcTicks % TimeSpan.FromMinutes(15).Ticks), TimeSpan.Zero).ToString("O", CultureInfo.InvariantCulture) : "none";
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{instrumentId:N}|{kind}|{timeframe}|{bucket}")))[..32];
    }

    private async Task<Guid?> ActiveJobIdAsync(Guid instrumentId, string kind, CancellationToken cancellationToken) =>
        await db.Jobs.AsNoTracking()
            .Where(j => j.InstrumentId == instrumentId && j.Kind == kind && (j.Status == AnalysisJobStatus.Pending || j.Status == AnalysisJobStatus.Processing))
            .Select(j => (Guid?)j.Id)
            .FirstOrDefaultAsync(cancellationToken);

    private AnalysisJobDto ToDto(AnalysisJob job) => new(
        job.Id, job.Symbol, job.Kind, job.Status, job.Attempt, job.CreatedAt, job.StartedAt, job.CompletedAt, job.ResultId, job.ErrorCode, job.ErrorDetail,
        AnalysisJobStatus.IsTerminal(job.Status) ? null : options.Value.PollAfterMs);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static AnalysisResultDto ToDto(AnalysisResult result, DateTimeOffset now)
    {
        var dto = JsonSerializer.Deserialize<AnalysisResultDto>(result.Parsed!, Json)!;
        return dto with { Id = result.Id, JobId = result.JobId, ValidationStatus = result.ValidationStatus, Stale = IsStale(result, now) };
    }

    private static bool IsStale(AnalysisResult result, DateTimeOffset now) => result.Stale || now - result.AnalysisAt > ResultLifetime;

    private static bool LevelsAccepted(AnalysisResult result) =>
        result.LevelValidation is { } json && JsonDocument.Parse(json).RootElement.GetProperty("accepted").GetBoolean();

    private static string EncodeCursor(AnalysisResult last) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes($"{last.AnalysisAt.UtcTicks.ToString(CultureInfo.InvariantCulture)}|{last.Id:N}"));

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
