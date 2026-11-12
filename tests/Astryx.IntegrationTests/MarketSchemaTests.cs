using Npgsql;

namespace Astryx.IntegrationTests;

/// <summary>Database-level guarantees of the <c>market</c> schema (SYSTEM_DESIGN.md §10.4, T-040).</summary>
public class MarketSchemaTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string CheckViolation = "23514";
    private const string UniqueViolation = "23505";

    [Fact]
    public async Task Symbol_is_unique_per_exchange_but_may_repeat_across_exchanges()
    {
        await using var connection = await OpenAsync();
        await InsertInstrumentAsync(connection, "DUPE", "XNAS");
        await InsertInstrumentAsync(connection, "DUPE", "XNYS");

        var error = await Should.ThrowAsync<PostgresException>(() => InsertInstrumentAsync(connection, "DUPE", "XNAS"));

        error.SqlState.ShouldBe(UniqueViolation);
        error.ConstraintName.ShouldBe("ix_instruments_symbol_exchange_mic");
    }

    [Theory]
    [InlineData("nvda", CheckViolation)]
    [InlineData("TOOLONGSYMBOL", "22001")] // varchar(12): value too long
    public async Task Symbols_must_be_upper_case_and_at_most_12_characters(string symbol, string sqlState)
    {
        await using var connection = await OpenAsync();

        var error = await Should.ThrowAsync<PostgresException>(() => InsertInstrumentAsync(connection, symbol, "XNAS"));

        error.SqlState.ShouldBe(sqlState);
    }

    [Theory]
    [InlineData("180.60", "186.77", "180.05", "186.40", null)] // design 04 NVDA daily bar
    [InlineData("10", "10", "10", "10", null)] // flat bar
    [InlineData("180.60", "186.77", "181.00", "186.40", "ck_price_bars_ohlc")] // low above open
    [InlineData("180.60", "186.00", "180.05", "186.40", "ck_price_bars_ohlc")] // high below close
    [InlineData("0", "1", "0", "1", "ck_price_bars_ohlc")] // non-positive low
    public async Task Price_bars_must_be_internally_consistent(string open, string high, string low, string close, string? violated)
    {
        await using var connection = await OpenAsync();
        var instrumentId = await InsertInstrumentAsync(connection, $"B{Guid.NewGuid():N}"[..8].ToUpperInvariant(), "XNAS");

        var insert = () => ExecuteAsync(
            connection,
            """
            INSERT INTO market.price_bars (instrument_id, timeframe, bar_start, session_date, open, high, low, close, volume, is_final, source, ingested_at)
            VALUES (@id, '1d', '2026-11-06T14:30:00Z', '2026-11-06', @o::numeric, @h::numeric, @l::numeric, @c::numeric, 301223000, true, 'fake', now())
            """,
            ("id", instrumentId), ("o", open), ("h", high), ("l", low), ("c", close));

        if (violated is null)
        {
            await insert();
            return;
        }

        var error = await Should.ThrowAsync<PostgresException>(insert);
        error.ConstraintName.ShouldBe(violated);
    }

    [Fact]
    public async Task Each_instrument_has_one_bar_per_timeframe_and_start()
    {
        await using var connection = await OpenAsync();
        var instrumentId = await InsertInstrumentAsync(connection, "BARPK", "XNAS");
        const string sql = """
            INSERT INTO market.price_bars (instrument_id, timeframe, bar_start, session_date, open, high, low, close, volume, is_final, source, ingested_at)
            VALUES (@id, @tf, '2026-11-06T14:30:00Z', '2026-11-06', 1, 2, 1, 2, 0, true, 'fake', now())
            """;
        await ExecuteAsync(connection, sql, ("id", instrumentId), ("tf", "1d"));
        await ExecuteAsync(connection, sql, ("id", instrumentId), ("tf", "1h"));

        var error = await Should.ThrowAsync<PostgresException>(() => ExecuteAsync(connection, sql, ("id", instrumentId), ("tf", "1d")));

        error.SqlState.ShouldBe(UniqueViolation);
    }

    [Fact]
    public async Task Quotes_reject_non_positive_prices_and_unknown_quality()
    {
        await using var connection = await OpenAsync();
        var instrumentId = await InsertInstrumentAsync(connection, "QTE", "XNAS");
        const string sql = """
            INSERT INTO market.quotes (instrument_id, last, open, high, low, prev_close, volume, as_of, received_at, source, quality, quote_seq)
            VALUES (@id, @last::numeric, 180.60, 186.77, 180.05, 180.24, 412800000, now(), now(), 'fake', @quality, 1)
            """;

        (await Should.ThrowAsync<PostgresException>(() => ExecuteAsync(connection, sql, ("id", instrumentId), ("last", "0"), ("quality", "delayed"))))
            .ConstraintName.ShouldBe("ck_quotes_last");
        (await Should.ThrowAsync<PostgresException>(() => ExecuteAsync(connection, sql, ("id", instrumentId), ("last", "186.40"), ("quality", "live"))))
            .ConstraintName.ShouldBe("ck_quotes_quality");
        await ExecuteAsync(connection, sql, ("id", instrumentId), ("last", "186.40"), ("quality", "synthetic"));
    }

    [Fact]
    public async Task Corporate_actions_need_their_ratio_or_amount()
    {
        await using var connection = await OpenAsync();
        var instrumentId = await InsertInstrumentAsync(connection, "CORP", "XNAS");
        const string sql = """
            INSERT INTO market.corporate_actions (id, instrument_id, action_type, ex_date, ratio, cash_amount, source)
            VALUES (gen_random_uuid(), @id, @type, @date::date, @ratio::numeric, @cash::numeric, 'fake')
            """;

        await ExecuteAsync(connection, sql, ("id", instrumentId), ("type", "split"), ("date", "2026-06-10"), ("ratio", "4"), ("cash", DBNull.Value));
        await ExecuteAsync(connection, sql, ("id", instrumentId), ("type", "cash_dividend"), ("date", "2026-06-11"), ("ratio", DBNull.Value), ("cash", "0.01"));
        (await Should.ThrowAsync<PostgresException>(() => ExecuteAsync(connection, sql, ("id", instrumentId), ("type", "split"), ("date", "2026-07-01"), ("ratio", DBNull.Value), ("cash", DBNull.Value))))
            .ConstraintName.ShouldBe("ck_corporate_actions_values");
        (await Should.ThrowAsync<PostgresException>(() => ExecuteAsync(connection, sql, ("id", instrumentId), ("type", "split"), ("date", "2026-06-10"), ("ratio", "2"), ("cash", DBNull.Value))))
            .SqlState.ShouldBe(UniqueViolation);
    }

    [Fact]
    public async Task Holidays_have_no_hours_and_trading_days_open_before_they_close()
    {
        await using var connection = await OpenAsync();
        const string sql = """
            INSERT INTO market.trading_calendar (exchange_mic, session_date, open_at, close_at, is_half_day, is_holiday)
            VALUES ('XNYS', @date::date, @open::timestamptz, @close::timestamptz, @half, @holiday)
            """;

        await ExecuteAsync(connection, sql, ("date", "2026-11-26"), ("open", DBNull.Value), ("close", DBNull.Value), ("half", false), ("holiday", true)); // Thanksgiving
        await ExecuteAsync(connection, sql, ("date", "2026-11-27"), ("open", "2026-11-27T14:30:00Z"), ("close", "2026-11-27T18:00:00Z"), ("half", true), ("holiday", false));
        (await Should.ThrowAsync<PostgresException>(() => ExecuteAsync(connection, sql, ("date", "2026-12-25"), ("open", "2026-12-25T14:30:00Z"), ("close", "2026-12-25T21:00:00Z"), ("half", false), ("holiday", true))))
            .ConstraintName.ShouldBe("ck_trading_calendar_hours");
        (await Should.ThrowAsync<PostgresException>(() => ExecuteAsync(connection, sql, ("date", "2026-12-01"), ("open", DBNull.Value), ("close", DBNull.Value), ("half", false), ("holiday", false))))
            .ConstraintName.ShouldBe("ck_trading_calendar_hours"); // a trading day needs hours
        (await Should.ThrowAsync<PostgresException>(() => ExecuteAsync(connection, sql, ("date", "2026-11-30"), ("open", "2026-11-30T21:00:00Z"), ("close", "2026-11-30T14:30:00Z"), ("half", false), ("holiday", false))))
            .ConstraintName.ShouldBe("ck_trading_calendar_hours");
    }

    [Theory]
    [InlineData("ix_instruments_symbol_trgm", "USING gin (symbol gin_trgm_ops)")]
    [InlineData("ix_instruments_name_trgm", "USING gin (name gin_trgm_ops)")]
    [InlineData("ix_news_articles_published_at", "(published_at DESC)")]
    [InlineData("ix_price_bars_timeframe_session_date", "(timeframe, session_date)")]
    [InlineData("ix_instrument_news_instrument_id_article_id", "(instrument_id, article_id)")]
    public async Task Search_and_lookup_indexes_exist(string index, string definitionFragment)
    {
        await using var connection = await OpenAsync();
        await using var command = new NpgsqlCommand("SELECT indexdef FROM pg_indexes WHERE schemaname = 'market' AND indexname = @name", connection);
        command.Parameters.AddWithValue("name", index);

        var definition = (string?)await command.ExecuteScalarAsync();

        definition.ShouldNotBeNull(index);
        definition.ShouldContain(definitionFragment);
    }

    [Fact]
    public async Task Deleting_an_instrument_removes_its_market_rows()
    {
        await using var connection = await OpenAsync();
        var instrumentId = await InsertInstrumentAsync(connection, "GONE", "XNAS");
        await ExecuteAsync(
            connection,
            "INSERT INTO market.fundamentals (instrument_id, as_of, source) VALUES (@id, now(), 'fake')",
            ("id", instrumentId));

        await ExecuteAsync(connection, "DELETE FROM market.instruments WHERE id = @id", ("id", instrumentId));

        await using var count = new NpgsqlCommand("SELECT count(*) FROM market.fundamentals WHERE instrument_id = @id", connection);
        count.Parameters.AddWithValue("id", instrumentId);
        ((long)(await count.ExecuteScalarAsync())!).ShouldBe(0);
    }

    private async Task<NpgsqlConnection> OpenAsync()
    {
        _ = factory.CreateClient(); // starting the host applies migrations
        var connection = new NpgsqlConnection(factory.ConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    private static async Task<Guid> InsertInstrumentAsync(NpgsqlConnection connection, string symbol, string mic)
    {
        var id = Guid.CreateVersion7();
        await ExecuteAsync(
            connection,
            "INSERT INTO market.instruments (id, symbol, exchange_mic, name, asset_type, updated_at) VALUES (@id, @symbol, @mic, 'Test Corp.', 'equity', now())",
            ("id", id), ("symbol", symbol), ("mic", mic));
        return id;
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql, params (string Name, object Value)[] parameters)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await command.ExecuteNonQueryAsync();
    }
}
