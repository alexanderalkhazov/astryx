using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Astryx.Modules.Ai.Application.Context;
using Astryx.Modules.Ai.Application.Prompts;
using Json.Schema;

namespace Astryx.Modules.Ai.Application.Validation;

/// <summary>What the validator checks the output against.</summary>
/// <param name="DataFresh">The context's <c>dataAsOf</c> is still within the freshness budget (§20.5).</param>
/// <param name="MinRewardRisk">The user's <c>min_reward_risk</c>.</param>
public sealed record ValidationInput(AnalysisContext Context, PromptTemplate Prompt, string Model, DateTimeOffset Now, bool DataFresh, decimal MinRewardRisk);

/// <summary>
/// The eight checks of SYSTEM_DESIGN.md §12.7, in this order: parse (fence strip, depth 16, 64 KB), forbidden fields
/// (dropped first, so the schema never sees them), schema, identity, temporal, numeric grounding, levels and content
/// policy. The model's output is untrusted throughout; the backend adds its own fields at the end.
/// </summary>
public sealed partial class AiOutputValidator
{
    public const int MaxBytes = 64 * 1024;
    public const int MaxDepth = 16;

    /// <summary>Indicator values may differ from the context by this fraction (rounding in prose).</summary>
    public const decimal GroundingTolerance = 0.005m;

    public const string Disclaimer = "AI-generated research for information only. Not investment advice. Not a guarantee of any outcome. Levels are suggestions that must pass deterministic risk checks.";

    /// <summary>Fields only the backend may set, plus sizing and order fields the model must never propose (step 8).</summary>
    private static readonly HashSet<string> Forbidden = new(StringComparer.OrdinalIgnoreCase)
    {
        "rewardRisk", "riskReward", "rewardToRisk", "positionSize", "quantity", "qty", "shares", "notional", "size",
        "orderType", "order", "orders", "side", "accepted", "levelValidation", "stale", "model", "promptVersion", "disclaimer",
    };

    private readonly Dictionary<string, JsonSchema> _schemas = [];
    private readonly Lock _gate = new();

    public ValidationResult Validate(string rawText, ValidationInput input)
    {
        var issues = new List<ValidationIssue>();

        // 1 Parse
        if (Encoding.UTF8.GetByteCount(rawText) > MaxBytes)
        {
            return Invalid(issues, ValidationIssueCodes.ParseError, "", $"Output is larger than {MaxBytes / 1024} KB.", repairable: false);
        }

        JsonObject analysis;
        try
        {
            var node = JsonNode.Parse(StripFence(rawText), documentOptions: new JsonDocumentOptions { MaxDepth = MaxDepth, AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow });
            if (node is not JsonObject obj)
            {
                return Invalid(issues, ValidationIssueCodes.ParseError, "", "Output is not a JSON object.", repairable: true);
            }

            analysis = obj;
        }
        catch (JsonException ex)
        {
            return Invalid(issues, ValidationIssueCodes.ParseError, "", $"Output is not valid JSON: {ex.Message}", repairable: true);
        }

        // 8 Forbidden fields (dropped before the schema check)
        DropForbidden(analysis, "", issues);

        // 2 Schema
        var evaluation = Schema(input.Prompt).Evaluate(JsonSerializer.SerializeToElement(analysis), new EvaluationOptions { OutputFormat = OutputFormat.List, RequireFormatValidation = true });
        if (!evaluation.IsValid)
        {
            var errors = (evaluation.Details ?? []).Where(d => d.Errors is { Count: > 0 })
                .SelectMany(d => d.Errors!.Select(e => new ValidationIssue(ValidationIssueCodes.SchemaError, d.InstanceLocation.ToString(), $"{Location(d.InstanceLocation.ToString())}: {e.Value}")))
                .DistinctBy(i => i.Message).Take(10).ToList();
            if (errors.Count == 0)
            {
                errors.Add(new ValidationIssue(ValidationIssueCodes.SchemaError, "", "Output does not match the schema."));
            }

            issues.AddRange(errors);
            return new ValidationResult(ValidationStatus.Invalid, null, issues, null, false, Repairable: true);
        }

        // 3 Identity
        var context = input.Context;
        if (!string.Equals(analysis["symbol"]!.GetValue<string>(), context.Symbol, StringComparison.Ordinal))
        {
            return Invalid(issues, ValidationIssueCodes.IdentityMismatch, "/symbol", $"symbol is {analysis["symbol"]}, expected {context.Symbol}.", repairable: false);
        }

        if (!DateTimeOffset.TryParse(analysis["dataTimestamp"]!.GetValue<string>(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dataTimestamp)
            || AnalysisContextBuilder.Timestamp(dataTimestamp) != AnalysisContextBuilder.Timestamp(context.DataAsOf))
        {
            return Invalid(issues, ValidationIssueCodes.IdentityMismatch, "/dataTimestamp", $"dataTimestamp is {analysis["dataTimestamp"]}, expected {AnalysisContextBuilder.Timestamp(context.DataAsOf)}.", repairable: false);
        }

        // 4 Temporal: the server's clock, never the model's
        analysis["analysisTimestamp"] = AnalysisContextBuilder.Timestamp(input.Now);
        var stale = !input.DataFresh;
        if (stale)
        {
            issues.Add(new ValidationIssue(ValidationIssueCodes.Stale, "/dataTimestamp", "Market data moved past its freshness budget during the analysis."));
        }

        // 5 Numeric grounding
        Ground(analysis, context, issues);

        // 6 Levels
        var levels = CheckLevels(analysis, context, input.MinRewardRisk, issues);

        // 7 Content policy
        Police(analysis, "", issues);

        analysis["rewardRisk"] = levels.RewardRisk?.ToString("F2", CultureInfo.InvariantCulture);
        analysis["levelValidation"] = new JsonObject { ["accepted"] = levels.Accepted, ["reasons"] = new JsonArray(levels.Reasons.Select(r => (JsonNode)r).ToArray()) };
        analysis["stale"] = stale;
        analysis["model"] = input.Model;
        analysis["promptVersion"] = $"{input.Prompt.Id}/{input.Prompt.Version}";
        analysis["disclaimer"] = Disclaimer;

        var status = issues.Any(i => i.Code != ValidationIssueCodes.Stale) ? ValidationStatus.ValidWithRejections : ValidationStatus.Valid;
        return new ValidationResult(status, analysis, issues, levels, stale, Repairable: false);
    }

    /// <summary>Removes a leading <c>```json</c> fence (and its closing fence) around the whole output.</summary>
    public static string StripFence(string text)
    {
        var trimmed = text.Trim();
        var match = Fence().Match(trimmed);
        return match.Success ? match.Groups["body"].Value : trimmed;
    }

    private JsonSchema Schema(PromptTemplate prompt)
    {
        lock (_gate)
        {
            var key = $"{prompt.Id}/{prompt.Version}";
            if (!_schemas.TryGetValue(key, out var schema))
            {
                schema = JsonSchema.FromText(prompt.Schema.RootElement.GetRawText(), new BuildOptions { SchemaRegistry = new SchemaRegistry() });
                _schemas[key] = schema;
            }

            return schema;
        }
    }

    private static ValidationResult Invalid(List<ValidationIssue> issues, string code, string path, string message, bool repairable)
    {
        issues.Add(new ValidationIssue(code, path, message));
        return new ValidationResult(ValidationStatus.Invalid, null, issues, null, false, repairable);
    }

    private static string Location(string pointer) => pointer.Length == 0 ? "(root)" : pointer;

    private static void DropForbidden(JsonNode node, string path, List<ValidationIssue> issues)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var key in obj.Select(p => p.Key).ToList())
                {
                    if (Forbidden.Contains(key))
                    {
                        obj.Remove(key);
                        issues.Add(new ValidationIssue(ValidationIssueCodes.ForbiddenField, $"{path}/{key}", $"Dropped {key}: the backend computes or owns it."));
                    }
                    else if (obj[key] is { } child)
                    {
                        DropForbidden(child, $"{path}/{key}", issues);
                    }
                }

                break;
            case JsonArray array:
                for (var i = 0; i < array.Count; i++)
                {
                    if (array[i] is { } item)
                    {
                        DropForbidden(item, $"{path}/{i}", issues);
                    }
                }

                break;
        }
    }

    private static void Ground(JsonObject analysis, AnalysisContext context, List<ValidationIssue> issues)
    {
        var known = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var section in new[] { "indicators", "quote" })
        {
            foreach (var (key, value) in context.Document[section]!.AsObject())
            {
                if (value is JsonValue v)
                {
                    known[key] = v.GetValueKind() == JsonValueKind.String ? v.GetValue<string>() : v.ToJsonString();
                }
            }
        }

        var indicators = analysis["relevantIndicators"]!.AsArray();
        for (var i = indicators.Count - 1; i >= 0; i--)
        {
            var name = indicators[i]!["name"]!.GetValue<string>();
            var value = indicators[i]!["value"]!.GetValue<string>();
            string? reason = null;
            if (!known.TryGetValue(name, out var expected))
            {
                reason = $"{name} is not in the context.";
            }
            else if (!Matches(value, expected))
            {
                reason = $"{name} is {value}, but the context says {expected}.";
            }

            if (reason is not null)
            {
                indicators.RemoveAt(i);
                issues.Add(new ValidationIssue(ValidationIssueCodes.UngroundedIndicator, $"/relevantIndicators/{i}", reason));
            }
        }

        var dates = new HashSet<string>(StringComparer.Ordinal);
        CollectDates(context.Document, dates);
        var catalysts = analysis["catalysts"]!.AsArray();
        for (var i = catalysts.Count - 1; i >= 0; i--)
        {
            if (catalysts[i]!["date"] is JsonValue date && date.GetValueKind() == JsonValueKind.String && !dates.Contains(date.GetValue<string>()))
            {
                issues.Add(new ValidationIssue(ValidationIssueCodes.UngroundedCatalyst, $"/catalysts/{i}", $"Catalyst date {date.GetValue<string>()} is not in the context."));
                catalysts.RemoveAt(i);
            }
        }
    }

    private static bool Matches(string value, string expected)
    {
        if (decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var v) && decimal.TryParse(expected, NumberStyles.Number, CultureInfo.InvariantCulture, out var e))
        {
            return e == 0 ? v == 0 : Math.Abs(v - e) <= Math.Abs(e) * GroundingTolerance;
        }

        return string.Equals(value.Trim(), expected, StringComparison.OrdinalIgnoreCase);
    }

    private static void CollectDates(JsonNode? node, HashSet<string> dates)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var (_, child) in obj)
                {
                    CollectDates(child, dates);
                }

                break;
            case JsonArray array:
                foreach (var child in array)
                {
                    CollectDates(child, dates);
                }

                break;
            case JsonValue value when value.GetValueKind() == JsonValueKind.String:
                var text = value.GetValue<string>();
                if (text.Length >= 10 && DateOnly.TryParseExact(text[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                {
                    dates.Add(text[..10]);
                }

                break;
        }
    }

    private static LevelValidation CheckLevels(JsonObject analysis, AnalysisContext context, decimal minRewardRisk, List<ValidationIssue> issues)
    {
        var suggested = analysis["suggestedLevels"]!.AsObject();
        LevelValidation result;
        if (analysis["insufficientData"]!.GetValue<bool>() || !suggested["justified"]!.GetValue<bool>())
        {
            result = new LevelValidation(false, ["The model did not suggest justified levels."], null);
        }
        else if (Price(suggested["entryZone"]?["low"]) is not { } low || Price(suggested["entryZone"]?["high"]) is not { } high
                 || Price(suggested["stopLoss"]) is not { } stop || Price(suggested["takeProfit"]) is not { } target)
        {
            result = new LevelValidation(false, ["Justified levels are incomplete."], null);
            issues.Add(new ValidationIssue(ValidationIssueCodes.LevelsRejected, "/suggestedLevels", result.Reasons[0]));
        }
        else
        {
            var atr = context.Document["indicators"]?["atr14"] is JsonValue a && decimal.TryParse(a.GetValue<string>(), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) ? parsed : (decimal?)null;
            result = LevelValidator.Validate(new SuggestedLevels(low, high, stop, target), context.PriceBandMin, context.PriceBandMax, atr, minRewardRisk);
            issues.AddRange(result.Reasons.Select(r => new ValidationIssue(ValidationIssueCodes.LevelsRejected, "/suggestedLevels", r)));
        }

        suggested["accepted"] = result.Accepted;
        return result;
    }

    private static decimal? Price(JsonNode? node) =>
        node is JsonValue v && v.GetValueKind() == JsonValueKind.String && decimal.TryParse(v.GetValue<string>(), NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : null;

    private static void Police(JsonNode node, string path, List<ValidationIssue> issues)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var key in obj.Select(p => p.Key).ToList())
                {
                    if (obj[key] is JsonValue v && v.GetValueKind() == JsonValueKind.String && ContentPolicy.Violates(v.GetValue<string>()))
                    {
                        obj[key] = ContentPolicy.Fallback;
                        issues.Add(new ValidationIssue(ValidationIssueCodes.PolicyViolation, $"{path}/{key}", "Replaced text that implied a guaranteed outcome."));
                    }
                    else if (obj[key] is { } child)
                    {
                        Police(child, $"{path}/{key}", issues);
                    }
                }

                break;
            case JsonArray array:
                for (var i = 0; i < array.Count; i++)
                {
                    if (array[i] is JsonValue v && v.GetValueKind() == JsonValueKind.String && ContentPolicy.Violates(v.GetValue<string>()))
                    {
                        array[i] = ContentPolicy.Fallback;
                        issues.Add(new ValidationIssue(ValidationIssueCodes.PolicyViolation, $"{path}/{i}", "Replaced text that implied a guaranteed outcome."));
                    }
                    else if (array[i] is { } child)
                    {
                        Police(child, $"{path}/{i}", issues);
                    }
                }

                break;
        }
    }

    [GeneratedRegex(@"^```[A-Za-z]*\s*\n(?<body>.*?)\n?```$", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex Fence();
}
