using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Astryx.Modules.Ai.Application.Context;

namespace Astryx.Modules.Ai.Application.Validation;

/// <summary>
/// §12.7 step 5b (TASKS.md T-165): numbers in the model's prose must come from the facts it was given. A price,
/// percentage or decimal in a sentence has to match a value in the context (as written, or as a percentage of a ratio),
/// the model's own suggested levels, the minimum reward-to-risk, or the distance of a known value from the last price. Periods ("14-day",
/// "RSI(14)"), counts up to ten, small multiples ("1.5× volume") and
/// years pass. A sentence with an unmatched number is removed, never shown.
/// </summary>
public static partial class ProseGrounding
{
    public const string RemovedText = "Part of this explanation was removed because a number in it didn't match the market data.";

    private static readonly HashSet<string> NotProse = new(StringComparer.Ordinal)
    {
        "schemaVersion", "symbol", "analysisTimestamp", "dataTimestamp", "overallSentiment", "status", "name", "value", "type", "date", "low", "high", "stopLoss", "takeProfit",
    };

    /// <summary>One number found in prose.</summary>
    public sealed record ProseNumber(decimal Value, decimal Tolerance, bool Exempt, string Text);

    /// <summary>Removes the sentences whose numbers aren't grounded; returns how many numbers were checked.</summary>
    public static int Check(JsonObject analysis, AnalysisContext context, LevelValidation levels, decimal minRewardRisk, List<ValidationIssue> issues)
    {
        var known = Known(analysis, context, levels, minRewardRisk);
        var checkedCount = 0;
        Walk(analysis, "", text =>
        {
            var sentences = Sentences().Split(text);
            var kept = new List<string>(sentences.Length);
            string? reason = null;
            foreach (var sentence in sentences)
            {
                var bad = Find(sentence).Where(n => !n.Exempt).FirstOrDefault(n =>
                {
                    checkedCount++;
                    return !known.Any(k => Math.Abs(k - n.Value) <= n.Tolerance);
                });
                if (bad is null)
                {
                    kept.Add(sentence);
                }
                else
                {
                    reason ??= $"\"{bad.Text}\" doesn't match any value in the context.";
                }
            }

            return reason is null ? (text, null) : (kept.Count == 0 ? RemovedText : string.Join(' ', kept), reason);
        }, issues);
        return checkedCount;
    }

    /// <summary>The numbers in a piece of text, with the tolerance their written precision allows.</summary>
    public static IReadOnlyList<ProseNumber> Find(string text)
    {
        var cleaned = IsoDate().Replace(text, " ");
        var found = new List<ProseNumber>();
        foreach (Match m in Number().Matches(cleaned))
        {
            var digits = m.Groups["int"].Value.Replace(",", "", StringComparison.Ordinal) + m.Groups["frac"].Value;
            if (!decimal.TryParse(digits, NumberStyles.Number, CultureInfo.InvariantCulture, out var value))
            {
                continue;
            }

            var places = m.Groups["frac"].Success ? m.Groups["frac"].Value.Length - 1 : 0;
            var scale = m.Groups["mult"].Value.Trim().ToUpperInvariant() switch
            {
                "K" or "THOUSAND" => 1_000m,
                "M" or "MILLION" => 1_000_000m,
                "B" or "BILLION" => 1_000_000_000m,
                _ => 1m,
            };
            var plainInteger = !m.Groups["cur"].Success && !m.Groups["pct"].Success && !m.Groups["frac"].Success && scale == 1m;
            var period = Period().IsMatch(cleaned[(m.Index + m.Length)..]) || (m.Index > 0 && cleaned[m.Index - 1] == '(');
            var multiple = !m.Groups["cur"].Success && !m.Groups["pct"].Success && scale == 1m && value <= 5m && Multiple().IsMatch(cleaned[(m.Index + m.Length)..]); // "1.5× volume", "2 ATR"
            var exempt = multiple || (plainInteger && (value <= 10m || period || value is >= 1900m and <= 2100m));
            var precision = 0.5m * scale / Pow10(places);
            found.Add(new ProseNumber(value * scale, Math.Max(value * scale * AiOutputValidator.GroundingTolerance, precision), exempt, m.Value.Trim()));
        }

        return found;
    }

    private static decimal Pow10(int places)
    {
        var result = 1m;
        for (var i = 0; i < places; i++)
        {
            result *= 10m;
        }

        return result;
    }

    private static List<decimal> Known(JsonObject analysis, AnalysisContext context, LevelValidation levels, decimal minRewardRisk)
    {
        var values = new List<decimal> { minRewardRisk };
        Collect(context.Document, values);
        var suggested = analysis["suggestedLevels"]?.AsObject();
        foreach (var node in new[] { suggested?["entryZone"]?["low"], suggested?["entryZone"]?["high"], suggested?["stopLoss"], suggested?["takeProfit"] })
        {
            if (node is JsonValue v && v.GetValueKind() == JsonValueKind.String && decimal.TryParse(v.GetValue<string>(), NumberStyles.Number, CultureInfo.InvariantCulture, out var d))
            {
                values.Add(d);
            }
        }

        if (levels.RewardRisk is { } rr)
        {
            values.Add(rr);
        }

        if (analysis["confidence"]?["value"] is JsonValue c && c.TryGetValue<decimal>(out var confidence))
        {
            values.Add(confidence);
        }

        var last = context.Document["quote"]?["last"] is JsonValue l && decimal.TryParse(l.ToString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var p) && p > 0m ? p : (decimal?)null;
        var derived = new List<decimal>();
        foreach (var v in values)
        {
            derived.Add(Math.Abs(v));
            derived.Add(Math.Abs(v * 100m)); // a ratio written as a percentage
            if (last is { } price && v > 0m)
            {
                derived.Add(Math.Abs((price - v) / v * 100m)); // "3.2% above its 50-day average"
                derived.Add(Math.Abs((v - price) / price * 100m)); // "the target is 9% above the price"
                derived.Add(Math.Abs(price - v));
            }
        }

        return derived;
    }

    private static void Collect(JsonNode? node, List<decimal> values)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var (_, child) in obj)
                {
                    Collect(child, values);
                }

                break;
            case JsonArray array:
                foreach (var child in array)
                {
                    Collect(child, values);
                }

                break;
            case JsonValue v when v.GetValueKind() == JsonValueKind.Number && v.TryGetValue<decimal>(out var d):
                values.Add(d);
                break;
            case JsonValue v when v.GetValueKind() == JsonValueKind.String && decimal.TryParse(v.GetValue<string>(), NumberStyles.Number, CultureInfo.InvariantCulture, out var d):
                values.Add(d);
                break;
        }
    }

    private static void Walk(JsonNode node, string path, Func<string, (string Text, string? Reason)> check, List<ValidationIssue> issues)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var key in obj.Select(p => p.Key).ToList())
                {
                    if (NotProse.Contains(key))
                    {
                        continue;
                    }

                    if (obj[key] is JsonValue v && v.GetValueKind() == JsonValueKind.String)
                    {
                        var (text, reason) = check(v.GetValue<string>());
                        if (reason is not null)
                        {
                            obj[key] = text;
                            issues.Add(new ValidationIssue(ValidationIssueCodes.UngroundedNumber, $"{path}/{key}", reason));
                        }
                    }
                    else if (obj[key] is { } child)
                    {
                        Walk(child, $"{path}/{key}", check, issues);
                    }
                }

                break;
            case JsonArray array:
                for (var i = array.Count - 1; i >= 0; i--)
                {
                    if (array[i] is JsonValue v && v.GetValueKind() == JsonValueKind.String)
                    {
                        var (_, reason) = check(v.GetValue<string>());
                        if (reason is not null)
                        {
                            array.RemoveAt(i); // a list item is one fact: drop it whole
                            issues.Add(new ValidationIssue(ValidationIssueCodes.UngroundedNumber, $"{path}/{i}", reason));
                        }
                    }
                    else if (array[i] is { } child)
                    {
                        Walk(child, $"{path}/{i}", check, issues);
                    }
                }

                break;
        }
    }

    [GeneratedRegex(@"^\s?(?:×|x\b|times\b|ATR\b)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex Multiple();

    [GeneratedRegex(@"^[-\s]?(?:day|week|month|year|session|bar|period|trading|hour|minute|quarter)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex Period();

    [GeneratedRegex(@"(?<=[.!?])\s+", RegexOptions.CultureInvariant)]
    private static partial Regex Sentences();

    [GeneratedRegex(@"\b\d{4}-\d{2}-\d{2}(?:[T ][\d:.]+Z?)?\b|\b\d{1,2}:\d{2}(?::\d{2})?\b", RegexOptions.CultureInvariant)]
    private static partial Regex IsoDate();

    [GeneratedRegex(@"(?<![\p{L}\d.,_])(?<cur>\$\s?)?(?<int>\d{1,3}(?:,\d{3})+|\d+)(?<frac>\.\d+)?(?<pct>\s?%)?(?<mult>\s?(?:[KMB]\b|thousand\b|million\b|billion\b))?", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex Number();
}
