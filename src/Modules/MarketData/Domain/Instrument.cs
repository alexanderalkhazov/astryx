namespace Astryx.Modules.MarketData.Domain;

/// <summary>Row in <c>market.instruments</c>: the instrument master (SYSTEM_DESIGN.md §10.4).</summary>
public sealed class Instrument
{
    public const int MaxSymbolLength = 12;

    public Guid Id { get; set; }

    /// <summary>Upper-case ticker, unique per exchange.</summary>
    public required string Symbol { get; set; }

    /// <summary>ISO 10383 market identifier, e.g. <c>XNAS</c>.</summary>
    public required string ExchangeMic { get; set; }

    public required string Name { get; set; }

    public string AssetType { get; set; } = Domain.AssetType.Equity;

    public string Currency { get; set; } = "USD";

    public string? Sector { get; set; }

    public string? Industry { get; set; }

    public decimal? MarketCap { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>JSON object mapping provider code to that provider's identifier.</summary>
    public string ProviderRefs { get; set; } = "{}";

    public DateTimeOffset UpdatedAt { get; set; }
}

public static class AssetType
{
    public const string Equity = "equity";
    public const string Etf = "etf";
    public const string Index = "index";
}
