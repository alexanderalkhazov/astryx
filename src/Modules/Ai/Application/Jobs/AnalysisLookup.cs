using System.Globalization;
using System.Text.Json;
using Astryx.Contracts.Ai;
using Astryx.Modules.Ai.Domain;
using Astryx.Modules.Ai.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Astryx.Modules.Ai.Application.Jobs;

/// <summary><see cref="IAnalysisLookup"/>: the user's usable analysis with its validated levels, for proposals (§7.9).</summary>
public sealed class AnalysisLookup(AiDbContext db, TimeProvider timeProvider) : IAnalysisLookup
{
    public async Task<AnalysisDetail?> GetAsync(Guid userId, Guid analysisId, CancellationToken cancellationToken)
    {
        var r = await db.Results.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == analysisId && x.UserId == userId && x.ValidationStatus != ValidationStatusNames.Invalid, cancellationToken);
        if (r?.Parsed is null)
        {
            return null;
        }

        var a = JsonDocument.Parse(r.Parsed).RootElement;
        var levels = a.GetProperty("suggestedLevels");
        decimal? Price(JsonElement e) => e.ValueKind == JsonValueKind.String && decimal.TryParse(e.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : null;
        var zone = levels.TryGetProperty("entryZone", out var z) && z.ValueKind == JsonValueKind.Object ? z : default;
        var accepted = a.GetProperty("levelValidation").GetProperty("accepted").GetBoolean();
        var stale = r.Stale || timeProvider.GetUtcNow() - r.AnalysisAt > AnalysisJobService.ResultLifetime;
        return new AnalysisDetail(
            r.Id, r.UserId, r.InstrumentId, r.Symbol, a.GetProperty("overallSentiment").GetString()!, stale, accepted,
            zone.ValueKind == JsonValueKind.Object ? Price(zone.GetProperty("low")) : null,
            zone.ValueKind == JsonValueKind.Object ? Price(zone.GetProperty("high")) : null,
            Price(levels.GetProperty("stopLoss")), Price(levels.GetProperty("takeProfit")),
            a.GetProperty("explanation").GetString()!,
            a.GetProperty("supportingEvidence").EnumerateArray().Select(e => e.GetString()!).ToList(),
            a.GetProperty("scenarios").GetProperty("bearish").GetProperty("conditions").EnumerateArray().Select(e => e.GetString()!).ToList(),
            r.AnalysisAt);
    }
}
