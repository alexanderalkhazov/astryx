using Astryx.Infrastructure.Observability;
using Npgsql;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Astryx.Api;

/// <summary>
/// Traces, metrics and error tracking (SYSTEM_DESIGN.md §23). OpenTelemetry exports over OTLP only when
/// <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> is set (compose points it at the Aspire dashboard); Sentry starts only when
/// <c>Sentry:Dsn</c> (env <c>Sentry__Dsn</c>) is set. Neither runs in tests or a bare <c>dotnet run</c>.
/// </summary>
public static class Observability
{
    public static WebApplicationBuilder AddAstryxObservability(this WebApplicationBuilder builder)
    {
        if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
        {
            builder.Services.AddOpenTelemetry()
                .ConfigureResource(resource => resource.AddService("astryx-api", serviceVersion: typeof(Observability).Assembly.GetName().Version?.ToString()))
                .WithTracing(tracing => tracing
                    .AddSource(AstryxTelemetry.Name)
                    .AddAspNetCoreInstrumentation(options => options.Filter = http => !http.Request.Path.StartsWithSegments("/health"))
                    .AddHttpClientInstrumentation()
                    .AddNpgsql()
                    .AddOtlpExporter())
                .WithMetrics(metrics => metrics
                    .AddMeter(AstryxTelemetry.Name)
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddNpgsqlInstrumentation()
                    .AddOtlpExporter());
        }

        if (builder.Configuration["Sentry:Dsn"] is { Length: > 0 } dsn)
        {
            builder.WebHost.UseSentry(options =>
            {
                options.Dsn = dsn;
                options.Environment = builder.Environment.EnvironmentName;
                options.SendDefaultPii = false; // no user emails, IPs or request bodies: credentials and balances stay out (§18.6)
                options.TracesSampleRate = 0; // traces go to OpenTelemetry
            });
        }

        return builder;
    }
}
