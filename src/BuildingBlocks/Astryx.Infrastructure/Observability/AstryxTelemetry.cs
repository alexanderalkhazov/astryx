using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Astryx.Infrastructure.Observability;

/// <summary>
/// The application's own traces and metrics (SYSTEM_DESIGN.md §23.3–23.4), exported by the host's OpenTelemetry
/// setup when an OTLP endpoint is configured. HTTP, EF/Npgsql and HttpClient metrics come from their instrumentation;
/// these are the domain ones. Names follow §23.4 (seconds, <c>_total</c> counters).
/// </summary>
public static class AstryxTelemetry
{
    public const string Name = "Astryx";

    public static readonly ActivitySource Source = new(Name);

    public static readonly Meter Meter = new(Name);

    // Durations are taken as TimeSpan: modules never handle double (the §20.1 float ban), the conversion lives here.
    private static readonly Histogram<double> AiInferenceDuration = Meter.CreateHistogram<double>("ai_inference_duration_seconds", "s", "One completion from the AI provider.");

    public static readonly Counter<long> AiJobs = Meter.CreateCounter<long>("ai_jobs_total", description: "Finished analysis jobs by outcome and error code.");

    public static readonly Counter<long> MarketProviderRequests = Meter.CreateCounter<long>("market_provider_requests_total", description: "Market data sync runs by provider, dataset and outcome.");

    private static readonly Histogram<double> ScannerRunDuration = Meter.CreateHistogram<double>("scanner_run_duration_seconds", "s", "One scanner run.");

    public static readonly Counter<long> Orders = Meter.CreateCounter<long>("orders_total", description: "Paper orders by purpose and outcome.");

    public static readonly Counter<long> RiskAssessments = Meter.CreateCounter<long>("risk_assessments_total", description: "Risk assessments by approvability and first failing check.");

    public static readonly Counter<long> JobsFailed = Meter.CreateCounter<long>("jobs_failed_total", description: "Job attempts that threw, by job type.");

    public static readonly Counter<long> ReconciliationMismatches = Meter.CreateCounter<long>("reconciliation_mismatches_total", description: "Nightly reconciliation issues by check.");

    private static double _scannerLastSuccess;
    private static double _quoteAgeMax;
    private static double _monitorLag;
    private static double _outboxLag;

    static AstryxTelemetry()
    {
        Meter.CreateObservableGauge("scanner_last_success_timestamp", () => Volatile.Read(ref _scannerLastSuccess), "s", "Unix time of the last successful scanner run.");
        Meter.CreateObservableGauge("market_quote_age_seconds", () => Volatile.Read(ref _quoteAgeMax), "s", "Age of the oldest stored quote after the latest sync.");
        Meter.CreateObservableGauge("position_monitor_lag_seconds", () => Volatile.Read(ref _monitorLag), "s", "Quote time to position-monitor evaluation, latest batch.");
        Meter.CreateObservableGauge("outbox_lag_seconds", () => Volatile.Read(ref _outboxLag), "s", "Event occurrence to dispatch, latest message.");
    }

    public static void AiInference(TimeSpan latency, string model, string promptVersion, string outcome) =>
        AiInferenceDuration.Record(latency.TotalSeconds, new("model", model), new("prompt_version", promptVersion), new("outcome", outcome));

    public static void ScannerRun(TimeSpan duration, string status) => ScannerRunDuration.Record(duration.TotalSeconds, new KeyValuePair<string, object?>("status", status));

    public static void ScannerSucceeded(DateTimeOffset at) => Volatile.Write(ref _scannerLastSuccess, at.ToUnixTimeMilliseconds() / 1000d);

    public static void QuoteAge(TimeSpan oldest) => Volatile.Write(ref _quoteAgeMax, Math.Max(0, oldest.TotalSeconds));

    public static void MonitorLag(TimeSpan lag) => Volatile.Write(ref _monitorLag, Math.Max(0, lag.TotalSeconds));

    public static void OutboxLag(TimeSpan lag) => Volatile.Write(ref _outboxLag, Math.Max(0, lag.TotalSeconds));
}
