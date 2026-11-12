namespace Astryx.Contracts.Ai;

/// <summary>
/// A validated AI analysis as a trade proposal may use it (§7.9, §12.6). Levels are present only when the model
/// justified them; <see cref="LevelsAccepted"/> is the deterministic LevelValidator's verdict. <see cref="Stale"/>
/// includes the 24 h lifetime.
/// </summary>
public sealed record AnalysisDetail(
    Guid Id, Guid UserId, Guid InstrumentId, string Symbol, string Sentiment, bool Stale, bool LevelsAccepted,
    decimal? EntryLow, decimal? EntryHigh, decimal? Stop, decimal? Target, string Explanation,
    IReadOnlyList<string> SupportingEvidence, IReadOnlyList<string> Invalidation, DateTimeOffset AnalysisAt);

/// <summary>Owned by Ai, read by Trading through Contracts.</summary>
public interface IAnalysisLookup
{
    /// <summary>The user's usable (not invalid) analysis, or null for an unknown id or another user's analysis.</summary>
    Task<AnalysisDetail?> GetAsync(Guid userId, Guid analysisId, CancellationToken cancellationToken);
}
