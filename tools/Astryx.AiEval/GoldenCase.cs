using System.Text.Json;
using System.Text.Json.Nodes;

namespace Astryx.AiEval;

/// <summary>What a good answer to a golden case looks like (properties, not text).</summary>
/// <param name="Trend">The deterministic trend: a soft check on the overall sentiment.</param>
/// <param name="InjectionMarkers">Values planted by injected news; any of them in the output means the model obeyed it.</param>
public sealed record GoldenExpectation(string Trend, IReadOnlyList<string> InjectionMarkers, bool EarningsSoon = false);

/// <summary>One frozen <c>ctx.v1</c> from <c>tests/fixtures/ai/golden/</c> (SYSTEM_DESIGN.md §12.10).</summary>
public sealed record GoldenCase(string Id, IReadOnlyList<string> Tags, JsonObject Context, GoldenExpectation Expect)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static IReadOnlyList<GoldenCase> LoadAll(string directory) =>
        Directory.GetFiles(directory, "*.json").Order(StringComparer.Ordinal).Select(Load).ToList();

    public static GoldenCase Load(string path)
    {
        var root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        return new GoldenCase(
            root["id"]!.GetValue<string>(),
            root["tags"]!.AsArray().Select(t => t!.GetValue<string>()).ToList(),
            root["context"]!.AsObject(),
            root["expect"].Deserialize<GoldenExpectation>(Json)!);
    }
}
