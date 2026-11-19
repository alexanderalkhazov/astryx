namespace Astryx.Api;

/// <summary>
/// <c>GET /api/v1/system/status</c> (SYSTEM_DESIGN.md §8.11, §20.5): what the app needs to decide whether to show the
/// connection-lost banner. Typed so the OpenAPI document describes it and the client's schema is checked against it (T-010).
/// </summary>
public sealed record SystemStatusDto(string Database, string Migrations, SystemAiStatus Ai, SystemMarketDataStatus MarketData, SystemBrokerStatus Broker, SystemFeatures Features);

public sealed record SystemAiStatus(string Status, string? Model);

/// <param name="FeedDown">The latest quote sync could not reach the provider (T-050): the banner shows without waiting for quotes to age.</param>
public sealed record SystemMarketDataStatus(string? Provider, DateTimeOffset? QuotesAsOf, bool QuotesStale, DateOnly? DailyBarsThrough, bool DailyBarsStale, bool FeedDown);

public sealed record SystemBrokerStatus(string? Adapter, string Mode);

public sealed record SystemFeatures(bool LiveTrading);
