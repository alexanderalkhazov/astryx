using System.Text.Json;
using Astryx.Api.Dev;
using Astryx.Api.OpenApi;
using Astryx.Infrastructure;
using Astryx.Infrastructure.Health;
using Astryx.Infrastructure.Realtime;
using Astryx.Modules.Ai;
using Astryx.Modules.Ai.Api;
using Astryx.Modules.Audit;
using Astryx.Modules.Discovery;
using Astryx.Modules.Discovery.Api;
using Astryx.Modules.Identity;
using Astryx.Modules.Identity.Api;
using Astryx.Modules.MarketData;
using Astryx.Modules.MarketData.Api;
using Astryx.Modules.MarketData.Application.Market;
using Astryx.Modules.Notifications;
using Astryx.Modules.Paper;
using Astryx.Modules.Paper.Api;
using Astryx.Modules.Portfolio;
using Astryx.Modules.Profiles;
using Astryx.Modules.Profiles.Api;
using Astryx.Modules.Quant;
using Astryx.Modules.Strategies;
using Astryx.Modules.Trading;
using Astryx.Modules.Trading.Api;
using Astryx.Modules.Watchlists;
using Astryx.Modules.Watchlists.Api;
using Astryx.Modules.Quant.Api;
using Astryx.Modules.Strategies.Api;
using Astryx.Workers;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Astryx.Infrastructure.Http;
using Astryx.Infrastructure.Persistence;
using Astryx.Infrastructure.Security;
using Astryx.Api.TestSupport;
using Serilog;
using Serilog.Formatting.Compact;

// `dotnet run --project apps/api -- --generate-jwt-key` prints a new ES256 entry for Jwt:SigningKeys and exits.
if (args.Contains("--generate-jwt-key", StringComparer.Ordinal))
{
    Console.WriteLine(Astryx.Modules.Identity.Application.JwtKeyRing.Generate());
    return;
}

var builder = WebApplication.CreateBuilder(args);

StartupGuards.EnsureLiveTradingDisabled(builder.Configuration);

builder.Services.AddSerilog((services, logger) => logger
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console(new RenderedCompactJsonFormatter()));

builder.Services.AddOpenApi(options => options.AddSchemaTransformer<DecimalStringSchemaTransformer>());
builder.Services.AddAstryxHttp(builder.Configuration);

builder.Services.AddAstryxPersistence(builder.Configuration);
builder.Services.AddRealtime();

builder.Services.AddHttpClient<OllamaHealthCheck>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Ai:Ollama:BaseUrl"] ?? "http://localhost:11434");
    client.Timeout = TimeSpan.FromSeconds(3);
});

builder.Services.AddHealthChecks()
    .AddCheck<OllamaHealthCheck>("ollama", tags: ["deps"]);

builder.Services
    .AddIdentityModule(builder.Configuration)
    .AddProfilesModule(builder.Configuration)
    .AddMarketDataModule(builder.Configuration)
    .AddDiscoveryModule(builder.Configuration)
    .AddWatchlistsModule(builder.Configuration)
    .AddQuantModule(builder.Configuration)
    .AddAiModule(builder.Configuration)
    .AddStrategiesModule(builder.Configuration)
    .AddTradingModule(builder.Configuration)
    .AddPaperModule(builder.Configuration)
    .AddPortfolioModule(builder.Configuration)
    .AddNotificationsModule(builder.Configuration)
    .AddAuditModule(builder.Configuration)
    .AddWorkers(builder.Configuration);

if (builder.Environment.IsEnvironment("Testing"))
{
    builder.Services.AddTestSupport();
}

var app = builder.Build();

// `dotnet Astryx.Api.dll --migrate` applies migrations and exits (production deploy step, SYSTEM_DESIGN.md §22.2).
if (args.Contains("--migrate", StringComparer.Ordinal))
{
    await MigrationRunner.MigrateAllAsync(app.Services);
    return;
}

if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    await MigrationRunner.MigrateAllAsync(app.Services);
}

app.UseAstryxHttp();
app.UseSerilogRequestLogging();
app.UseAuthentication();
app.UseUserScope();
app.UseRateLimiter();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = c => c.Tags.Contains("ready"), ResponseWriter = WriteHealthJson });
app.MapHealthChecks("/health/deps", new HealthCheckOptions
{
    Predicate = _ => true,
    ResponseWriter = WriteHealthJson,
    ResultStatusCodes = { [HealthStatus.Degraded] = StatusCodes.Status200OK },
});

app.MapGet("/api/v1/system/status", async (HealthCheckService health, FreshnessPolicy freshness, IConfiguration config, CancellationToken ct) =>
{
    var report = await health.CheckHealthAsync(ct);
    var market = await freshness.StatusAsync(ct);
    string Status(string name) => report.Entries.TryGetValue(name, out var e) ? e.Status.ToString().ToLowerInvariant() : "unknown";
    return Results.Ok(new
    {
        database = Status("postgres"),
        migrations = Status("migrations"),
        ai = new { status = Status("ollama"), model = config["Ai:Ollama:Model"] },
        marketData = new
        {
            provider = config["MarketData:Provider"],
            quotesAsOf = market.Quotes.AsOf,
            quotesStale = market.Quotes.Stale,
            dailyBarsThrough = market.DailyBarsThrough,
            dailyBarsStale = market.DailyBarsStale,
            feedDown = market.FeedDown,
        },
        broker = new { adapter = config["Broker:Adapter"], mode = "paper" },
        features = new { liveTrading = config.GetValue<bool>("Features:LiveTrading") },
    });
});

app.MapIdentityEndpoints();
app.MapProfileEndpoints();
app.MapMarketEndpoints();
app.MapSearchEndpoints();
app.MapWatchlistEndpoints();
app.MapIndicatorEndpoints();
app.MapStrategyEndpoints();
app.MapScannerEndpoints();
app.MapAnalysisEndpoints();
app.MapProposalEndpoints();
app.MapPaperOrderEndpoints();
app.MapPositionEndpoints();
app.MapRealtime();

if (DevEndpoints.IsEnabled(app.Environment, app.Configuration))
{
    app.MapDevEndpoints();
}

if (app.Environment.IsEnvironment("Testing"))
{
    app.MapTestSupportEndpoints();
}

app.Run();

static Task WriteHealthJson(HttpContext context, HealthReport report)
{
    context.Response.ContentType = "application/json";
    var body = new
    {
        status = report.Status.ToString(),
        entries = report.Entries.ToDictionary(
            e => e.Key,
            e => new { status = e.Value.Status.ToString(), description = e.Value.Description, data = e.Value.Data }),
    };
    return context.Response.WriteAsync(JsonSerializer.Serialize(body, JsonSerializerOptions.Web));
}

public partial class Program;
