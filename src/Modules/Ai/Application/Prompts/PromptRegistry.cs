using System.Reflection;
using System.Text.Json;
using Astryx.Modules.Ai.Application.Context;
using Astryx.Modules.Ai.Application.Providers;
using Microsoft.Extensions.Options;
using Scriban;
using Scriban.Runtime;

namespace Astryx.Modules.Ai.Application.Prompts;

/// <summary><c>Ai:Prompts</c>: the active version per prompt id, so a rollback needs no deploy (SYSTEM_DESIGN.md §12.5).</summary>
public sealed class PromptOptions
{
    public const string SectionName = "Ai:Prompts";

    public Dictionary<string, string> Active { get; } = new(StringComparer.Ordinal);
}

/// <summary>
/// One released prompt version: <c>system.md</c>, <c>user.scriban</c> and <c>schema.json</c> from
/// <c>Prompts/&lt;id&gt;/&lt;version&gt;/</c>. Immutable once released; a change is a new version folder.
/// </summary>
public sealed class PromptTemplate(string id, string version, string system, Template user, JsonDocument schema)
{
    public string Id { get; } = id;

    public string Version { get; } = version;

    public string System { get; } = system;

    /// <summary>The output contract, e.g. <c>analysis.v1</c> (the schema's <c>schemaVersion</c> const).</summary>
    public string SchemaVersion { get; } = schema.RootElement.GetProperty("properties").GetProperty("schemaVersion").GetProperty("const").GetString()!;

    public JsonDocument Schema { get; } = schema;

    /// <summary>System and user messages for a context. The same context always renders the same text.</summary>
    public IReadOnlyList<AiMessage> Render(AnalysisContext context)
    {
        var news = new ScriptArray();
        foreach (var item in context.News)
        {
            news.Add(new ScriptObject
            {
                ["published_at"] = AnalysisContextBuilder.Timestamp(item.PublishedAt),
                ["source"] = item.Source,
                ["headline"] = item.Headline,
                ["snippet"] = item.Snippet,
            });
        }

        var model = new ScriptObject
        {
            ["symbol"] = context.Symbol,
            ["name"] = context.Name,
            ["data_as_of"] = AnalysisContextBuilder.Timestamp(context.DataAsOf),
            ["context_json"] = context.PromptJson(),
            ["news"] = news,
        };
        var templateContext = new TemplateContext { StrictVariables = true, EnableRelaxedMemberAccess = false };
        templateContext.PushGlobal(model);
        return [AiMessage.System(System), AiMessage.User(user.Render(templateContext).Trim())];
    }
}

/// <summary>Resolves <c>(promptId, version)</c> from the embedded prompt folders; every active version must exist.</summary>
public sealed class PromptRegistry
{
    public const string StockAnalysis = "stock_analysis";

    private const string Prefix = "Astryx.Ai.Prompts/";

    private readonly Dictionary<(string Id, string Version), PromptTemplate> prompts;
    private readonly IReadOnlyDictionary<string, string> active;

    public PromptRegistry(IOptions<PromptOptions> options)
    {
        prompts = Load(typeof(PromptRegistry).Assembly);
        active = options.Value.Active;
        foreach (var (id, version) in active)
        {
            if (!prompts.ContainsKey((id, version)))
            {
                throw new InvalidOperationException($"Ai:Prompts:{id} is {version}, but no prompt folder Prompts/{id}/{version} is embedded.");
            }
        }
    }

    public IEnumerable<(string Id, string Version)> Versions => prompts.Keys.OrderBy(k => k.Id, StringComparer.Ordinal).ThenBy(k => k.Version, StringComparer.Ordinal);

    public PromptTemplate Get(string id, string version) =>
        prompts.TryGetValue((id, version), out var prompt) ? prompt : throw new KeyNotFoundException($"No prompt {id}/{version}.");

    /// <summary>The configured version of a prompt.</summary>
    public PromptTemplate GetActive(string id) =>
        active.TryGetValue(id, out var version) ? Get(id, version) : throw new InvalidOperationException($"Ai:Prompts:{id} is not configured.");

    private static Dictionary<(string, string), PromptTemplate> Load(Assembly assembly)
    {
        var files = assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith(Prefix, StringComparison.Ordinal))
            .Select(n => (Resource: n, Parts: n[Prefix.Length..].Replace('\\', '/').Split('/')))
            .Where(f => f.Parts.Length == 3)
            .GroupBy(f => (f.Parts[0], f.Parts[1]));

        var result = new Dictionary<(string, string), PromptTemplate>();
        foreach (var folder in files)
        {
            var byName = folder.ToDictionary(f => f.Parts[2], f => Read(assembly, f.Resource), StringComparer.Ordinal);
            if (!byName.TryGetValue("system.md", out var system) || !byName.TryGetValue("user.scriban", out var user) || !byName.TryGetValue("schema.json", out var schema))
            {
                throw new InvalidOperationException($"Prompt {folder.Key.Item1}/{folder.Key.Item2} needs system.md, user.scriban and schema.json.");
            }

            var template = Template.Parse(user, $"{folder.Key.Item1}/{folder.Key.Item2}/user.scriban");
            if (template.HasErrors)
            {
                throw new InvalidOperationException($"Prompt {folder.Key.Item1}/{folder.Key.Item2}: {string.Join("; ", template.Messages)}");
            }

            result[folder.Key] = new PromptTemplate(folder.Key.Item1, folder.Key.Item2, system.Trim(), template, JsonDocument.Parse(schema));
        }

        return result;
    }

    private static string Read(Assembly assembly, string resource)
    {
        using var stream = assembly.GetManifestResourceStream(resource)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
