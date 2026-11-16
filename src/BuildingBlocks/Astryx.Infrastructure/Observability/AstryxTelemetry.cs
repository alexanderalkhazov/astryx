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

    public static readonly Counter<long> JobsDead = Meter.CreateCounter<long>("jobs_dead_total", description: "Jobs dead-lettered (out of attempts, unknown type or bad payload), by job type.");

    public static readonly Counter<long> AuthLoginFailures = Meter.CreateCounter<long>("auth_login_failures_total", description: "Failed sign-ins by reason.");

    public static readonly Counter<long> AuthRefreshReuse = Meter.CreateCounter<long>("auth_refresh_reuse_total", description: "Refresh-token reuse detections (family revoked), by reason.");

    public static readonly Counter<long> ReconciliationMismatches = Meter.CreateCounter<long>("reconciliation_mismatches_total", description: "Nightly reconciliation issues by check.");

    private static double _scannerLastSuccess;
    private static double _quoteAgeMax;
    private static double _monitorLag;
    private static double _outboxLag;
    private static long _liveTrading;
    private static long _sessionOpen;
    private static Func<bool>? _aiCircuitOpen;

    static AstryxTelemetry()
    {
        Meter.CreateObservableGauge("scanner_last_success_timestamp", () => Volatile.Read(ref _scannerLastSuccess), "s", "Unix time of the last successful scanner run.");
        Meter.CreateObservableGauge("market_quote_age_seconds", () => Volatile.Read(ref _quoteAgeMax), "s", "Age of the oldest stored quote after the latest sync.");
        Meter.CreateObservableGauge("position_monitor_lag_seconds", () => Volatile.Read(ref _monitorLag), "s", "Quote time to position-monitor evaluation, latest batch.");
        Meter.CreateObservableGauge("outbox_lag_seconds", () => Volatile.Read(ref _outboxLag), "s", "Event occurrence to dispatch, latest message.");
        Meter.CreateObservableGauge("market_session_open", () => Interlocked.Read(ref _sessionOpen), description: "1 during the regular session: alerts that only matter while trading key on it.");
        Meter.CreateObservableGauge("live_trading_enabled", () => Interlocked.Read(ref _liveTrading), description: "1 when Features:LiveTrading is on: must stay 0 in the MVP.");
        Meter.CreateObservableGauge("ai_circuit_open", () => _aiCircuitOpen?.Invoke() == true ? 1 : 0, description: "1 while the AI provider's circuit breaker is open.");
    }

    public static void AiInference(TimeSpan latency, string model, string promptVersion, string outcome) =>
        AiInferenceDuration.Record(latency.TotalSeconds, new("model", model), new("prompt_version", promptVersion), new("outcome", outcome));

    public static void ScannerRun(TimeSpan duration, string status) => ScannerRunDuration.Record(duration.TotalSeconds, new KeyValuePair<string, object?>("status", status));

    public static void SessionOpen(bool open) => Interlocked.Exchange(ref _sessionOpen, open ? 1 : 0);

    public static void LiveTrading(bool enabled) => Interlocked.Exchange(ref _liveTrading, enabled ? 1 : 0);

    public static void ObserveAiCircuit(Func<bool> isOpen) => _aiCircuitOpen = isOpen;

    public static void ScannerSucceeded(DateTimeOffset at) => Volatile.Write(ref _scannerLastSuccess, at.ToUnixTimeMilliseconds() / 1000d);

    public static void QuoteAge(TimeSpan oldest) => Volatile.Write(ref _quoteAgeMax, Math.Max(0, oldest.TotalSeconds));

    public static void MonitorLag(TimeSpan lag) => Volatile.Write(ref _monitorLag, Math.Max(0, lag.TotalSeconds));

    public static void OutboxLag(TimeSpan lag) => Volatile.Write(ref _outboxLag, Math.Max(0, lag.TotalSeconds));
}
