using Astryx.Infrastructure.Persistence;
using Astryx.Modules.MarketData.Domain;
using Microsoft.EntityFrameworkCore;

namespace Astryx.Modules.MarketData.Infrastructure.Persistence;

/// <summary>
/// MarketData module data in schema <c>market</c> (SYSTEM_DESIGN.md §10.4). Quant shares the schema with its own
/// context and history table (<c>indicator_snapshots</c>).
/// </summary>
public sealed class MarketDataDbContext(DbContextOptions<MarketDataDbContext> options) : ModuleDbContext(options)
{
    public const string SchemaName = "market";

    public override string Schema => SchemaName;

    public DbSet<Instrument> Instruments => Set<Instrument>();

    public DbSet<Quote> Quotes => Set<Quote>();

    public DbSet<PriceBar> PriceBars => Set<PriceBar>();

    public DbSet<CorporateAction> CorporateActions => Set<CorporateAction>();

    public DbSet<Fundamentals> Fundamentals => Set<Fundamentals>();

    public DbSet<NewsArticle> NewsArticles => Set<NewsArticle>();

    public DbSet<InstrumentNews> InstrumentNews => Set<InstrumentNews>();

    public DbSet<TradingSession> TradingCalendar => Set<TradingSession>();

    public DbSet<ProviderSyncState> ProviderSyncState => Set<ProviderSyncState>();

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Instrument>(entity =>
        {
            entity.ToTable("instruments", t =>
            {
                t.HasCheckConstraint("ck_instruments_symbol", "symbol = upper(symbol) AND char_length(symbol) BETWEEN 1 AND 12");
                t.HasCheckConstraint("ck_instruments_asset_type", "asset_type IN ('equity','etf','index')");
                t.HasCheckConstraint("ck_instruments_market_cap", "market_cap IS NULL OR market_cap >= 0");
            });
            entity.HasKey(i => i.Id);
            entity.Property(i => i.Symbol).HasMaxLength(Instrument.MaxSymbolLength);
            entity.Property(i => i.ExchangeMic).HasMaxLength(4);
            entity.Property(i => i.AssetType).HasMaxLength(8);
            entity.Property(i => i.Currency).HasColumnType("character(3)").HasDefaultValue("USD");
            entity.Property(i => i.MarketCap).HasPrecision(20, 2);
            entity.Property(i => i.IsActive).HasDefaultValue(true);
            entity.Property(i => i.ProviderRefs).HasColumnType("jsonb").HasDefaultValueSql("'{}'::jsonb");
            entity.HasIndex(i => new { i.Symbol, i.ExchangeMic }).IsUnique();
            // Search (§11.4 M1): ticker prefix and fuzzy company name.
            entity.HasIndex(i => i.Symbol).HasMethod("gin").HasOperators("gin_trgm_ops").HasDatabaseName("ix_instruments_symbol_trgm");
            entity.HasIndex(i => i.Name).HasMethod("gin").HasOperators("gin_trgm_ops").HasDatabaseName("ix_instruments_name_trgm");
            entity.HasIndex(i => i.Sector);
        });

        modelBuilder.Entity<Quote>(entity =>
        {
            entity.ToTable("quotes", t =>
            {
                t.HasCheckConstraint("ck_quotes_last", "last > 0");
                t.HasCheckConstraint("ck_quotes_quality", "quality IN ('realtime','delayed','eod','synthetic')");
                t.HasCheckConstraint("ck_quotes_volume", "volume >= 0");
            });
            entity.HasKey(q => q.InstrumentId);
            foreach (var price in new[] { nameof(Quote.Last), nameof(Quote.Bid), nameof(Quote.Ask), nameof(Quote.Open), nameof(Quote.High), nameof(Quote.Low), nameof(Quote.PrevClose) })
            {
                entity.Property(price).HasPrecision(18, 6);
            }

            entity.Property(q => q.Source).HasMaxLength(32);
            entity.Property(q => q.Quality).HasMaxLength(16);
            entity.HasOne<Instrument>().WithOne().HasForeignKey<Quote>(q => q.InstrumentId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PriceBar>(entity =>
        {
            entity.ToTable("price_bars", t =>
            {
                t.HasCheckConstraint("ck_price_bars_timeframe", "timeframe IN ('1m','5m','15m','1h','1d')");
                t.HasCheckConstraint("ck_price_bars_ohlc", "low > 0 AND low <= least(open, close) AND high >= greatest(open, close)");
                t.HasCheckConstraint("ck_price_bars_volume", "volume >= 0");
            });
            entity.HasKey(b => new { b.InstrumentId, b.Timeframe, b.BarStart });
            foreach (var price in new[] { nameof(PriceBar.Open), nameof(PriceBar.High), nameof(PriceBar.Low), nameof(PriceBar.Close) })
            {
                entity.Property(price).HasPrecision(18, 6);
            }

            entity.Property(b => b.Timeframe).HasMaxLength(4);
            entity.Property(b => b.Source).HasMaxLength(32);
            entity.HasIndex(b => new { b.Timeframe, b.SessionDate });
            entity.HasOne<Instrument>().WithMany().HasForeignKey(b => b.InstrumentId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CorporateAction>(entity =>
        {
            entity.ToTable("corporate_actions", t =>
            {
                t.HasCheckConstraint("ck_corporate_actions_type", "action_type IN ('split','reverse_split','cash_dividend')");
                // A split needs its ratio and a dividend its amount; adjustment (§20.7) can't proceed without them. IS NOT NULL is
                // explicit because a CHECK that evaluates to NULL passes.
                t.HasCheckConstraint(
                    "ck_corporate_actions_values",
                    "(action_type = 'cash_dividend' AND cash_amount IS NOT NULL AND cash_amount > 0) OR (action_type IN ('split','reverse_split') AND ratio IS NOT NULL AND ratio > 0)");
            });
            entity.HasKey(c => c.Id);
            entity.Property(c => c.ActionType).HasMaxLength(16);
            entity.Property(c => c.Ratio).HasPrecision(18, 8);
            entity.Property(c => c.CashAmount).HasPrecision(18, 6);
            entity.Property(c => c.Source).HasMaxLength(32);
            entity.HasIndex(c => new { c.InstrumentId, c.ActionType, c.ExDate }).IsUnique();
            entity.HasOne<Instrument>().WithMany().HasForeignKey(c => c.InstrumentId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Fundamentals>(entity =>
        {
            entity.ToTable("fundamentals");
            entity.HasKey(f => f.InstrumentId);
            entity.Property(f => f.Beta).HasPrecision(9, 4);
            entity.Property(f => f.PeRatio).HasPrecision(12, 4);
            entity.Property(f => f.EpsTtm).HasPrecision(18, 6);
            entity.Property(f => f.Data).HasColumnType("jsonb").HasDefaultValueSql("'{}'::jsonb");
            entity.Property(f => f.Source).HasMaxLength(32);
            entity.HasOne<Instrument>().WithOne().HasForeignKey<Fundamentals>(f => f.InstrumentId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<NewsArticle>(entity =>
        {
            entity.ToTable("news_articles", t => t.HasCheckConstraint("ck_news_articles_headline", "char_length(headline) BETWEEN 1 AND 500"));
            entity.HasKey(n => n.Id);
            entity.Property(n => n.Provider).HasMaxLength(32);
            entity.Property(n => n.Headline).HasMaxLength(NewsArticle.MaxHeadlineLength);
            entity.HasIndex(n => new { n.Provider, n.ProviderArticleId }).IsUnique();
            entity.HasIndex(n => n.PublishedAt).IsDescending();
        });

        modelBuilder.Entity<InstrumentNews>(entity =>
        {
            entity.ToTable("instrument_news");
            entity.HasKey(n => new { n.ArticleId, n.InstrumentId });
            entity.HasIndex(n => new { n.InstrumentId, n.ArticleId });
            entity.HasOne<NewsArticle>().WithMany().HasForeignKey(n => n.ArticleId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<Instrument>().WithMany().HasForeignKey(n => n.InstrumentId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TradingSession>(entity =>
        {
            entity.ToTable("trading_calendar", t => t.HasCheckConstraint(
                "ck_trading_calendar_hours",
                "(is_holiday AND open_at IS NULL AND close_at IS NULL AND NOT is_half_day) OR (NOT is_holiday AND open_at IS NOT NULL AND close_at IS NOT NULL AND open_at < close_at)"));
            entity.HasKey(s => new { s.ExchangeMic, s.SessionDate });
            entity.Property(s => s.ExchangeMic).HasMaxLength(4);
        });

        modelBuilder.Entity<ProviderSyncState>(entity =>
        {
            entity.ToTable("provider_sync_state", t => t.HasCheckConstraint("ck_provider_sync_state_counters", "consecutive_failures >= 0 AND requests_today >= 0"));
            entity.HasKey(s => new { s.Provider, s.Dataset });
            entity.Property(s => s.Provider).HasMaxLength(32);
            entity.Property(s => s.Dataset).HasMaxLength(32);
            entity.Property(s => s.LastErrorCode).HasMaxLength(64);
        });
    }
}
