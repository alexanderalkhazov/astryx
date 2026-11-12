using System.Globalization;
using System.Text.Json;
using Astryx.Contracts.Events.Ai;
using Astryx.Contracts.Profiles;
using Astryx.Modules.Ai.Application.Analysis;
using Astryx.Modules.Ai.Application.Context;
using Astryx.Modules.Ai.Application.Prompts;
using Astryx.Modules.Ai.Application.Providers;
using Astryx.Modules.Ai.Application.Validation;
using Astryx.Modules.Ai.Domain;
using Astryx.Modules.Ai.Infrastructure.Persistence;
using Astryx.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Astryx.Modules.Ai.Application.Jobs;

/// <summary>
/// The <c>AiAnalysisWorker</c> step (SYSTEM_DESIGN.md §12.8, §19.2): claims a due job from <c>ai.analysis_jobs</c>
/// with a lease (or reclaims one whose lease expired), builds the context, calls the model, validates, and settles
/// only while it still holds the lease. Provider failures retry with backoff; everything else is final. Cancellation is
/// checked before the model call and before the result is saved. Logs carry no prompt or output text (§12.9).
/// </summary>
public sealed class AnalysisJobProcessor(IServiceProvider services, IOptions<AnalysisJobOptions> options, IOptions<AiGenerationOptions> generation, TimeProvider timeProvider, ILogger<AnalysisJobProcessor> logger)
{
    private const string UnavailableDetail = "The local AI model is unavailable. Indicators are still available.";
    private const string InvalidDetail = "Analysis couldn't be generated reliably. Indicators are still available.";

    /// <summary>Lease: every model call in an attempt (with the repair retry) plus the grace period.</summary>
    public TimeSpan Lease => TimeSpan.FromSeconds((generation.Value.TimeoutSeconds * AnalysisGenerator.MaxAttempts) + options.Value.LeaseGraceSeconds);

    /// <summary>Processes the next due job. Returns false when nothing was due.</summary>
    public async Task<bool> RunOnceAsync(string workerId, CancellationToken cancellationToken)
    {
        await FailExhaustedLeasesAsync(cancellationToken);
        if (await ClaimAsync(workerId, cancellationToken) is not { } job)
        {
            return false;
        }

        await ProcessAsync(job, workerId, cancellationToken);
        return true;
    }

    public async Task<AnalysisJob?> ClaimAsync(string workerId, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AiDbContext>();
        var now = timeProvider.GetUtcNow();
        var until = now + Lease;
        var claimed = await db.Jobs.FromSql($"""
                UPDATE ai.analysis_jobs
                SET status = 'processing', locked_by = {workerId}, locked_until = {until}, attempt = attempt + 1, started_at = COALESCE(started_at, {now})
                WHERE id = (
                    SELECT id FROM ai.analysis_jobs
                    WHERE (status = 'pending' AND run_after <= {now})
                       OR (status = 'processing' AND locked_until < {now} AND attempt < max_attempts)
                    ORDER BY run_after, id
                    FOR UPDATE SKIP LOCKED
                    LIMIT 1)
                RETURNING *
                """)
            .IgnoreQueryFilters()
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        if (claimed.SingleOrDefault() is not { } job)
        {
            return null;
        }

        db.Publish(Updated(job, AnalysisJobStatus.Processing, null, null));
        await db.SaveChangesAsync(cancellationToken);
        return job;
    }

    /// <summary>A processing job whose lease expired with no attempts left: the worker died on its last try.</summary>
    public async Task<int> FailExhaustedLeasesAsync(CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AiDbContext>();
        var now = timeProvider.GetUtcNow();
        return await db.Jobs.IgnoreQueryFilters()
            .Where(j => j.Status == AnalysisJobStatus.Processing && j.LockedUntil < now && j.Attempt >= j.MaxAttempts)
            .ExecuteUpdateAsync(
                s => s.SetProperty(j => j.Status, AnalysisJobStatus.Failed).SetProperty(j => j.CompletedAt, now).SetProperty(j => j.LockedBy, (string?)null)
                    .SetProperty(j => j.ErrorCode, AnalysisErrorCodes.LeaseExpired).SetProperty(j => j.ErrorDetail, "The analysis stopped responding. Try again."),
                cancellationToken);
    }

    public async Task ProcessAsync(AnalysisJob job, string workerId, CancellationToken cancellationToken)
    {
        using var logScope = logger.BeginScope(new Dictionary<string, object?> { ["AnalysisJobId"] = job.Id, ["Attempt"] = job.Attempt });
        await using var scope = services.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AiDbContext>();

        if (await CancelRequestedAsync(db, job.Id, cancellationToken))
        {
            await SettleAsync(db, job, workerId, AnalysisJobStatus.Cancelled, AnalysisErrorCodes.Cancelled, "Cancelled.", null, cancellationToken);
            return;
        }

        var built = await sp.GetRequiredService<AnalysisContextBuilder>().BuildAsync(job.Symbol, timeProvider.GetUtcNow(), cancellationToken);
        if (!built.IsSuccess)
        {
            await SettleAsync(db, job, workerId, AnalysisJobStatus.Failed, built.Error.Code, built.Error.Message, null, cancellationToken);
            return;
        }

        var context = built.Value;
        var prompt = sp.GetRequiredService<PromptRegistry>().GetActive(PromptRegistry.StockAnalysis);
        var limits = await sp.GetRequiredService<IRiskSettingsSource>().GetAsync(job.UserId, cancellationToken);
        var provider = sp.GetRequiredService<IAiProvider>();
        var started = timeProvider.GetUtcNow();
        GenerationResult generated;
        try
        {
            generated = await sp.GetRequiredService<AnalysisGenerator>().GenerateAsync(context, prompt, limits.MinRewardRisk, cancellationToken);
        }
        catch (AiProviderUnavailableException ex)
        {
            await LogFailureAsync(db, job, provider.Name, prompt, started, ex.Reason, cancellationToken);
            var timeout = ex.Reason == "timeout";
            if (job.Attempt < job.MaxAttempts)
            {
                var retryAt = timeProvider.GetUtcNow() + Backoff(job.Attempt);
                await RequeueAsync(db, job, workerId, retryAt, cancellationToken);
                logger.LogWarning("AI analysis attempt {Attempt} failed ({Reason}); retrying at {RetryAt}", job.Attempt, ex.Reason, retryAt);
            }
            else
            {
                await SettleAsync(db, job, workerId, AnalysisJobStatus.Failed, timeout ? AnalysisErrorCodes.Timeout : AnalysisErrorCodes.ProviderUnavailable,
                    timeout ? "The AI model took too long to respond. Indicators are still available." : UnavailableDetail, null, cancellationToken);
                logger.LogError("AI analysis failed after {Attempts} attempts ({Reason})", job.Attempt, ex.Reason);
            }

            return;
        }

        await LogAttemptsAsync(db, job, provider.Name, prompt, generated, cancellationToken);
        if (await CancelRequestedAsync(db, job.Id, cancellationToken))
        {
            await SettleAsync(db, job, workerId, AnalysisJobStatus.Cancelled, AnalysisErrorCodes.Cancelled, "Cancelled.", null, cancellationToken);
            return;
        }

        var result = ToResult(job, context, prompt, provider.Name, generated);
        var validation = generated.Validation;
        var ok = validation.Status != ValidationStatus.Invalid;
        await SettleAsync(db, job, workerId, ok ? AnalysisJobStatus.Completed : AnalysisJobStatus.Failed,
            ok ? null : AnalysisErrorCodes.OutputInvalid, ok ? null : InvalidDetail, result, cancellationToken);
        logger.LogInformation("AI analysis finished: {Status} with {Issues} issue(s) in {Attempts} call(s)", validation.Status, validation.Issues.Count, generated.Attempts.Count);
    }

    private TimeSpan Backoff(int attempt)
    {
        var steps = options.Value.BackoffSeconds;
        var seconds = steps[Math.Clamp(attempt - 1, 0, steps.Length - 1)];
        var jitterMs = Random.Shared.Next(-(seconds * 200), (seconds * 200) + 1); // ±20 %
        return TimeSpan.FromMilliseconds((seconds * 1000) + jitterMs);
    }

    private static Task<bool> CancelRequestedAsync(AiDbContext db, Guid jobId, CancellationToken cancellationToken) =>
        db.Jobs.IgnoreQueryFilters().AnyAsync(j => j.Id == jobId && j.CancelRequestedAt != null, cancellationToken);

    private AnalysisResult ToResult(AnalysisJob job, AnalysisContext context, PromptTemplate prompt, string provider, GenerationResult generated)
    {
        var validation = generated.Validation;
        var last = generated.Attempts[^1].Completion;
        var analysis = validation.Analysis;
        var raw = last.RawText.Length > AnalysisResult.MaxRawOutput ? last.RawText[..AnalysisResult.MaxRawOutput] : last.RawText;
        return new AnalysisResult
        {
            Id = Ids.New(timeProvider),
            JobId = job.Id,
            UserId = job.UserId,
            InstrumentId = job.InstrumentId,
            Symbol = job.Symbol,
            Provider = provider,
            Model = generated.Model.Name,
            ModelDigest = generated.Model.Digest,
            PromptId = prompt.Id,
            PromptVersion = prompt.Version,
            OutputSchemaVersion = prompt.SchemaVersion,
            Context = context.ToJson(),
            RawOutput = raw,
            Parsed = analysis?.ToJsonString(),
            ValidationStatus = validation.Status switch
            {
                ValidationStatus.Valid => ValidationStatusNames.Valid,
                ValidationStatus.ValidWithRejections => ValidationStatusNames.ValidWithRejections,
                _ => ValidationStatusNames.Invalid,
            },
            ValidationIssues = JsonSerializer.Serialize(validation.Issues.Select(i => new { path = i.Path, code = i.Code, message = i.Message })),
            Sentiment = analysis?["overallSentiment"]?.GetValue<string>(),
            Confidence = analysis?["confidence"]?["value"]?.GetValue<decimal>() is { } c ? Math.Round(c, 3, MidpointRounding.ToEven) : null,
            DataAsOf = context.DataAsOf,
            AnalysisAt = timeProvider.GetUtcNow(),
            LevelValidation = validation.Levels is { } levels ? JsonSerializer.Serialize(new { accepted = levels.Accepted, reasons = levels.Reasons }) : null,
            RewardRisk = validation.Levels?.RewardRisk,
            Stale = validation.Stale,
            PromptTokens = generated.Attempts.Sum(a => a.Completion.PromptTokens ?? 0),
            CompletionTokens = generated.Attempts.Sum(a => a.Completion.CompletionTokens ?? 0),
            LatencyMs = (int)generated.Attempts.Sum(a => a.Completion.Latency.TotalMilliseconds),
        };
    }

    private async Task LogAttemptsAsync(AiDbContext db, AnalysisJob job, string provider, PromptTemplate prompt, GenerationResult generated, CancellationToken cancellationToken)
    {
        var at = timeProvider.GetUtcNow();
        foreach (var attempt in generated.Attempts)
        {
            var first = attempt.Validation.Issues.FirstOrDefault()?.Code;
            var outcome = attempt.Validation.Status != ValidationStatus.Invalid ? AiRequestOutcome.Ok
                : first == ValidationIssueCodes.SchemaError ? AiRequestOutcome.SchemaError : AiRequestOutcome.ParseError;
            db.Requests.Add(new AiRequest
            {
                Id = Ids.New(timeProvider),
                JobId = job.Id,
                Attempt = job.Attempt,
                Provider = provider,
                Model = attempt.Completion.Model,
                PromptVersion = $"{prompt.Id}/{prompt.Version}",
                StartedAt = at - attempt.Completion.Latency,
                DurationMs = (int)attempt.Completion.Latency.TotalMilliseconds,
                PromptTokens = attempt.Completion.PromptTokens,
                CompletionTokens = attempt.Completion.CompletionTokens,
                Outcome = outcome,
                ErrorMessage = outcome == AiRequestOutcome.Ok ? null : Truncate(attempt.Validation.FirstError),
            });
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task LogFailureAsync(AiDbContext db, AnalysisJob job, string provider, PromptTemplate prompt, DateTimeOffset started, string reason, CancellationToken cancellationToken)
    {
        var status = reason.StartsWith("http_", StringComparison.Ordinal) && int.TryParse(reason.AsSpan(5, 3), NumberStyles.None, CultureInfo.InvariantCulture, out var code) ? code : (int?)null;
        db.Requests.Add(new AiRequest
        {
            Id = Ids.New(timeProvider),
            JobId = job.Id,
            Attempt = job.Attempt,
            Provider = provider,
            Model = "unknown",
            PromptVersion = $"{prompt.Id}/{prompt.Version}",
            StartedAt = started,
            DurationMs = (int)(timeProvider.GetUtcNow() - started).TotalMilliseconds,
            Outcome = reason == "timeout" ? AiRequestOutcome.Timeout : AiRequestOutcome.HttpError,
            HttpStatus = status,
            ErrorMessage = Truncate(reason),
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    private static string? Truncate(string? text) => text is null || text.Length <= 500 ? text : text[..500];

    public static AnalysisUpdated Updated(AnalysisJob job, string status, Guid? resultId, string? errorCode) =>
        new(job.UserId, job.Id, job.Symbol, status, job.Attempt, resultId, errorCode);

    private async Task RequeueAsync(AiDbContext db, AnalysisJob job, string workerId, DateTimeOffset retryAt, CancellationToken cancellationToken)
    {
        var requeued = await db.Jobs.IgnoreQueryFilters()
            .Where(j => j.Id == job.Id && j.LockedBy == workerId && j.Status == AnalysisJobStatus.Processing)
            .ExecuteUpdateAsync(
                s => s.SetProperty(j => j.Status, AnalysisJobStatus.Pending).SetProperty(j => j.RunAfter, retryAt)
                    .SetProperty(j => j.LockedBy, (string?)null).SetProperty(j => j.LockedUntil, (DateTimeOffset?)null)
                    .SetProperty(j => j.ErrorCode, AnalysisErrorCodes.ProviderUnavailable).SetProperty(j => j.ErrorDetail, UnavailableDetail),
                cancellationToken);
        if (requeued == 1)
        {
            db.Publish(Updated(job, AnalysisJobStatus.Pending, null, AnalysisErrorCodes.ProviderUnavailable));
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Saves the result (if any) and finishes the job in one transaction, only while this worker holds the lease. A
    /// worker that lost its lease changes nothing; the unique <c>job_id</c> keeps it to one result row.
    /// </summary>
    private async Task SettleAsync(AiDbContext db, AnalysisJob job, string workerId, string status, string? errorCode, string? errorDetail, AnalysisResult? result, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var linked = result is not null && status == AnalysisJobStatus.Completed ? result.Id : (Guid?)null;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var settled = await db.Jobs.IgnoreQueryFilters()
            .Where(j => j.Id == job.Id && j.LockedBy == workerId && j.Status == AnalysisJobStatus.Processing)
            .ExecuteUpdateAsync(
                s => s.SetProperty(j => j.Status, status).SetProperty(j => j.CompletedAt, now)
                    .SetProperty(j => j.LockedBy, (string?)null).SetProperty(j => j.LockedUntil, (DateTimeOffset?)null)
                    .SetProperty(j => j.ErrorCode, errorCode).SetProperty(j => j.ErrorDetail, errorDetail).SetProperty(j => j.ResultId, linked),
                cancellationToken);
        if (settled == 0)
        {
            logger.LogWarning("AI analysis job lost its lease before settling; another worker owns it");
            return;
        }

        if (result is not null)
        {
            db.Results.Add(result);
        }

        db.Publish(Updated(job, status, linked, errorCode));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
