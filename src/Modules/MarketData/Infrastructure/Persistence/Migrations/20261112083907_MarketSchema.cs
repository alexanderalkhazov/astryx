using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Astryx.Modules.MarketData.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MarketSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "market");

            migrationBuilder.CreateTable(
                name: "instruments",
                schema: "market",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    symbol = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    exchange_mic = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    asset_type = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    currency = table.Column<string>(type: "character(3)", nullable: false, defaultValue: "USD"),
                    sector = table.Column<string>(type: "text", nullable: true),
                    industry = table.Column<string>(type: "text", nullable: true),
                    market_cap = table.Column<decimal>(type: "numeric(20,2)", precision: 20, scale: 2, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    provider_refs = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_instruments", x => x.id);
                    table.CheckConstraint("ck_instruments_asset_type", "asset_type IN ('equity','etf','index')");
                    table.CheckConstraint("ck_instruments_market_cap", "market_cap IS NULL OR market_cap >= 0");
                    table.CheckConstraint("ck_instruments_symbol", "symbol = upper(symbol) AND char_length(symbol) BETWEEN 1 AND 12");
                });

            migrationBuilder.CreateTable(
                name: "news_articles",
                schema: "market",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    provider_article_id = table.Column<string>(type: "text", nullable: false),
                    source_name = table.Column<string>(type: "text", nullable: false),
                    headline = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    url = table.Column<string>(type: "text", nullable: false),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    provider_summary = table.Column<string>(type: "text", nullable: true),
                    ingested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_news_articles", x => x.id);
                    table.CheckConstraint("ck_news_articles_headline", "char_length(headline) BETWEEN 1 AND 500");
                });

            migrationBuilder.CreateTable(
                name: "provider_sync_state",
                schema: "market",
                columns: table => new
                {
                    provider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    dataset = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    last_success_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_error_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    consecutive_failures = table.Column<int>(type: "integer", nullable: false),
                    requests_today = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_provider_sync_state", x => new { x.provider, x.dataset });
                    table.CheckConstraint("ck_provider_sync_state_counters", "consecutive_failures >= 0 AND requests_today >= 0");
                });

            migrationBuilder.CreateTable(
                name: "trading_calendar",
                schema: "market",
                columns: table => new
                {
                    exchange_mic = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    session_date = table.Column<DateOnly>(type: "date", nullable: false),
                    open_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    close_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    is_half_day = table.Column<bool>(type: "boolean", nullable: false),
                    is_holiday = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_trading_calendar", x => new { x.exchange_mic, x.session_date });
                    table.CheckConstraint("ck_trading_calendar_hours", "(is_holiday AND open_at IS NULL AND close_at IS NULL AND NOT is_half_day) OR (NOT is_holiday AND open_at IS NOT NULL AND close_at IS NOT NULL AND open_at < close_at)");
                });

            migrationBuilder.CreateTable(
                name: "corporate_actions",
                schema: "market",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    instrument_id = table.Column<Guid>(type: "uuid", nullable: false),
                    action_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ex_date = table.Column<DateOnly>(type: "date", nullable: false),
                    ratio = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: true),
                    cash_amount = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_corporate_actions", x => x.id);
                    table.CheckConstraint("ck_corporate_actions_type", "action_type IN ('split','reverse_split','cash_dividend')");
                    table.CheckConstraint("ck_corporate_actions_values", "(action_type = 'cash_dividend' AND cash_amount IS NOT NULL AND cash_amount > 0) OR (action_type IN ('split','reverse_split') AND ratio IS NOT NULL AND ratio > 0)");
                    table.ForeignKey(
                        name: "fk_corporate_actions_instruments_instrument_id",
                        column: x => x.instrument_id,
                        principalSchema: "market",
                        principalTable: "instruments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "fundamentals",
                schema: "market",
                columns: table => new
                {
                    instrument_id = table.Column<Guid>(type: "uuid", nullable: false),
                    as_of = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    beta = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: true),
                    pe_ratio = table.Column<decimal>(type: "numeric(12,4)", precision: 12, scale: 4, nullable: true),
                    eps_ttm = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    next_earnings_date = table.Column<DateOnly>(type: "date", nullable: true),
                    data = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fundamentals", x => x.instrument_id);
                    table.ForeignKey(
                        name: "fk_fundamentals_instruments_instrument_id",
                        column: x => x.instrument_id,
                        principalSchema: "market",
                        principalTable: "instruments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "price_bars",
                schema: "market",
                columns: table => new
                {
                    instrument_id = table.Column<Guid>(type: "uuid", nullable: false),
                    timeframe = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    bar_start = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    session_date = table.Column<DateOnly>(type: "date", nullable: false),
                    open = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    high = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    low = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    close = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    volume = table.Column<long>(type: "bigint", nullable: false),
                    is_final = table.Column<bool>(type: "boolean", nullable: false),
                    source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ingested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_price_bars", x => new { x.instrument_id, x.timeframe, x.bar_start });
                    table.CheckConstraint("ck_price_bars_ohlc", "low > 0 AND low <= least(open, close) AND high >= greatest(open, close)");
                    table.CheckConstraint("ck_price_bars_timeframe", "timeframe IN ('1m','5m','15m','1h','1d')");
                    table.CheckConstraint("ck_price_bars_volume", "volume >= 0");
                    table.ForeignKey(
                        name: "fk_price_bars_instruments_instrument_id",
                        column: x => x.instrument_id,
                        principalSchema: "market",
                        principalTable: "instruments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "quotes",
                schema: "market",
                columns: table => new
                {
                    instrument_id = table.Column<Guid>(type: "uuid", nullable: false),
                    last = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    bid = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    ask = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    open = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    high = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    low = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    prev_close = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    volume = table.Column<long>(type: "bigint", nullable: false),
                    as_of = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    quality = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    quote_seq = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_quotes", x => x.instrument_id);
                    table.CheckConstraint("ck_quotes_last", "last > 0");
                    table.CheckConstraint("ck_quotes_quality", "quality IN ('realtime','delayed','eod','synthetic')");
                    table.CheckConstraint("ck_quotes_volume", "volume >= 0");
                    table.ForeignKey(
                        name: "fk_quotes_instruments_instrument_id",
                        column: x => x.instrument_id,
                        principalSchema: "market",
                        principalTable: "instruments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "instrument_news",
                schema: "market",
                columns: table => new
                {
                    article_id = table.Column<Guid>(type: "uuid", nullable: false),
                    instrument_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_instrument_news", x => new { x.article_id, x.instrument_id });
                    table.ForeignKey(
                        name: "fk_instrument_news_instruments_instrument_id",
                        column: x => x.instrument_id,
                        principalSchema: "market",
                        principalTable: "instruments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_instrument_news_news_articles_article_id",
                        column: x => x.article_id,
                        principalSchema: "market",
                        principalTable: "news_articles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_corporate_actions_instrument_id_action_type_ex_date",
                schema: "market",
                table: "corporate_actions",
                columns: new[] { "instrument_id", "action_type", "ex_date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_instrument_news_instrument_id_article_id",
                schema: "market",
                table: "instrument_news",
                columns: new[] { "instrument_id", "article_id" });

            migrationBuilder.CreateIndex(
                name: "ix_instruments_name_trgm",
                schema: "market",
                table: "instruments",
                column: "name")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_instruments_sector",
                schema: "market",
                table: "instruments",
                column: "sector");

            migrationBuilder.CreateIndex(
                name: "ix_instruments_symbol_exchange_mic",
                schema: "market",
                table: "instruments",
                columns: new[] { "symbol", "exchange_mic" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_instruments_symbol_trgm",
                schema: "market",
                table: "instruments",
                column: "symbol")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_news_articles_provider_provider_article_id",
                schema: "market",
                table: "news_articles",
                columns: new[] { "provider", "provider_article_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_news_articles_published_at",
                schema: "market",
                table: "news_articles",
                column: "published_at",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "ix_price_bars_timeframe_session_date",
                schema: "market",
                table: "price_bars",
                columns: new[] { "timeframe", "session_date" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "corporate_actions",
                schema: "market");

            migrationBuilder.DropTable(
                name: "fundamentals",
                schema: "market");

            migrationBuilder.DropTable(
                name: "instrument_news",
                schema: "market");

            migrationBuilder.DropTable(
                name: "price_bars",
                schema: "market");

            migrationBuilder.DropTable(
                name: "provider_sync_state",
                schema: "market");

            migrationBuilder.DropTable(
                name: "quotes",
                schema: "market");

            migrationBuilder.DropTable(
                name: "trading_calendar",
                schema: "market");

            migrationBuilder.DropTable(
                name: "news_articles",
                schema: "market");

            migrationBuilder.DropTable(
                name: "instruments",
                schema: "market");
        }
    }
}
