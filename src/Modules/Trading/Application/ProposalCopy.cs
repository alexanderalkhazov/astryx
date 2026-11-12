using System.Globalization;
using Astryx.Contracts.Ai;
using Astryx.Contracts.Discovery;

namespace Astryx.Modules.Trading.Application;

/// <summary>
/// Display copy for proposals (§7.9). From a signal it is deterministic template text, the same wording as the mobile
/// Signal tab (T-068). From an analysis it is the model's validated text, labelled <c>ai</c>.
/// </summary>
public static class ProposalCopy
{
    private static readonly Dictionary<string, string> Headlines = new(StringComparer.Ordinal)
    {
        ["breakout"] = "Breaking out of its base",
        ["momentum"] = "A momentum leader pushing higher",
        ["pullback"] = "An orderly pullback in an uptrend",
        ["unusual_volume"] = "Trading on unusual volume",
        ["earnings"] = "Drifting higher after earnings",
    };

    public static (string Headline, string Summary, IReadOnlyList<string> Evidence, IReadOnlyList<string> Invalidation) FromSignal(SignalDetail s)
    {
        var evidence = new List<string>();
        if (s.RelVolume is { } rv)
        {
            evidence.Add($"Volume {Fixed(rv, 1)}× its 20-day average");
        }

        if (s.RelStrength is { } rs)
        {
            evidence.Add($"Relative strength {rs} of 99 across the scanner universe");
        }

        if (s.RewardRisk is { } rr)
        {
            evidence.Add($"Reward:risk {Fixed(rr, 2)} from entry {Price(s.Entry)} to target {Price(s.Target)}");
        }

        if (s.Rsi14 is { } rsi)
        {
            evidence.Add($"RSI(14) at {Fixed(rsi, 1)}");
        }

        var entryWord = s.OrderType == "buy_stop" ? "buy stop" : "entry";
        IReadOnlyList<string> invalidation =
        [
            $"A close below the stop at {Price(s.Stop)}.",
            $"The {entryWord} at {Price(s.Entry)} lapses at the end of the session unless a newer scan confirms it.",
            "A newer scan that no longer finds the setup retires the signal.",
        ];
        return (Headlines.GetValueOrDefault(s.Type, "A scanner setup"), s.Reason, evidence, invalidation);
    }

    public static (string Headline, string Summary, IReadOnlyList<string> Evidence, IReadOnlyList<string> Invalidation) FromAnalysis(AnalysisDetail a)
    {
        var headline = $"{CultureInfo.InvariantCulture.TextInfo.ToTitleCase(a.Sentiment)} case from the AI analysis";
        var summary = a.Explanation.Length <= 2000 ? a.Explanation : a.Explanation[..2000];
        return (headline, summary, a.SupportingEvidence, a.Invalidation);
    }

    public static string Price(decimal value) => value.ToString("F2", CultureInfo.InvariantCulture);

    private static string Fixed(string value, int places) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var d)
            ? Math.Round(d, places, MidpointRounding.ToEven).ToString("F" + places.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture)
            : value;
}
