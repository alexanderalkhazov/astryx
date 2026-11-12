using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Astryx.Modules.Strategies.Infrastructure.Seed;

/// <summary>
/// SQL that seeds the five design strategies from the embedded <c>catalog.json</c> (written by
/// <c>tools/gen_strategy_seed.py</c>, SYSTEM_DESIGN.md §7.8). Ids are derived from the strategy code, so they are the
/// same in every database and links stay stable. Used by the <c>StrategyCatalog</c> migration.
/// </summary>
public static class CatalogSeed
{
    private const string Resource = "Astryx.Strategies.Seed.catalog.json";
    private static readonly DateTimeOffset SeededAt = new(2026, 6, 30, 22, 0, 0, TimeSpan.Zero);

    public static Guid StrategyId(string code) => Id($"strategy:{code}");

    public static Guid VersionId(string code, int number) => Id($"strategy-version:{code}:{number}");

    public static Guid RunId(string code, int runNumber) => Id($"backtest-run:{code}:{runNumber}");

    public static IReadOnlyList<string> Statements()
    {
        using var stream = typeof(CatalogSeed).Assembly.GetManifestResourceStream(Resource)
            ?? throw new InvalidOperationException($"Embedded resource {Resource} is missing.");
        var catalog = JsonNode.Parse(stream)!["strategies"]!.AsArray();
        var statements = new List<string>();
        var order = 0;
        foreach (var node in catalog)
        {
            var s = node!.AsObject();
            var code = (string)s["code"]!;
            var strategyId = StrategyId(code);
            var versions = s["versions"]!.AsArray();
            var published = versions.First(v => (string)v!["state"]! == "published")!; // exactly one per strategy (seed tests)
            var publishedNumber = (int)published["number"]!;

            statements.Add(
                $"""
                INSERT INTO strategy.strategies (id, code, name, description, rules, param_schema, status, owner_user_id, published_version_id, live_since, illustrative_live, display_order, updated_at)
                VALUES ('{strategyId}', {Text(code)}, {Text((string)s["name"]!)}, {Text((string)s["description"]!)}, {Json(s["rules"])}, {Json(s["schema"])},
                        {Text((string)s["status"]!)}, NULL, NULL, '{(string)s["liveSince"]!}', {Json(s["live"])}, {order++}, '{SeededAt:O}')
                ON CONFLICT (id) DO NOTHING;
                """);

            foreach (var v in versions)
            {
                var number = (int)v!["number"]!;
                statements.Add(
                    $"""
                    INSERT INTO strategy.strategy_versions (id, strategy_id, version_number, state, params, code_version, created_by, created_at)
                    VALUES ('{VersionId(code, number)}', '{strategyId}', {number}, {Text((string)v["state"]!)}, {Json(v["params"])}, {Text($"{code}@1")}, NULL, '{SeededAt.AddDays(number - 4):O}')
                    ON CONFLICT (id) DO NOTHING;
                    """);
            }

            statements.Add($"UPDATE strategy.strategies SET published_version_id = '{VersionId(code, publishedNumber)}' WHERE id = '{strategyId}' AND published_version_id IS NULL;");

            var run = s["run"]!.AsObject();
            var runNumber = (int)run["runNumber"]!;
            statements.Add(
                $"""
                INSERT INTO strategy.backtest_runs (id, strategy_id, strategy_version_id, run_number, status, period_start, period_end, oos_start, initial_capital,
                                                    assumptions, metrics, equity_curve, drawdown_curve, monthly_returns, started_at, finished_at, error_code)
                VALUES ('{RunId(code, runNumber)}', '{strategyId}', '{VersionId(code, publishedNumber)}', {runNumber}, 'completed',
                        '{(string)run["periodStart"]!}', '{(string)run["periodEnd"]!}', '{(string)run["oosStart"]!}', {(string)run["initialCapital"]!},
                        {Json(run["assumptions"])}, {Json(run["metrics"])}, {Json(run["equityCurve"])}, {Json(run["drawdownCurve"])}, {Json(run["monthlyReturns"])},
                        '{(string)run["finishedAt"]!}', '{(string)run["finishedAt"]!}', NULL)
                ON CONFLICT (id) DO NOTHING;
                """);
        }

        return statements;
    }

    /// <summary>A SQL string literal (single quotes doubled).</summary>
    private static string Text(string value) => $"'{value.Replace("'", "''", StringComparison.Ordinal)}'";

    private static string Json(JsonNode? node) => Text(node?.ToJsonString(new JsonSerializerOptions { WriteIndented = false }) ?? "null") + "::jsonb";

    /// <summary>A stable id from a key (MD5-based, like a name-based UUID).</summary>
    private static Guid Id(string key) => new(MD5.HashData(Encoding.UTF8.GetBytes(key)));
}
