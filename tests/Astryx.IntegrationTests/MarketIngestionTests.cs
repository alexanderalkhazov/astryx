using System.Text.Json;
using Astryx.Contracts.MarketData;
using Astryx.Infrastructure.Jobs;
using Astryx.Modules.MarketData.Application.Ingestion;
using Astryx.Modules.MarketData.Application.Providers;
using Astryx.Modules.MarketData.Domain;
using Astryx.Modules.MarketData.Infrastructure.Persistence;
using Astryx.Modules.MarketData.Infrastructure.Providers.Fake;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Npgsql;

namespace Astryx.IntegrationTests;

/// <summary>Priority symbols a test controls, standing in for positions and watchlists.</summary>
public sealed class TestPrioritySymbols : IPrioritySymbolSource
{
    public List<string> Symbols { get; } = [];

    public int Priority => 0;

    public Task<IReadOnlyList<string>> GetSymbolsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<string>>(Symbols.ToList());
}

/// <summary>A host on a fake clock at the design's "Updated 14:32 ET" moment, with ingestion handlers callable directly.</summary>
public class IngestionApiFactory : ApiFactory
{
    // Monday 9 Nov 2026 14:32 EST: the regular session is open and Friday 6 Nov is the latest closed session.
    public static readonly DateTimeOffset DesignMoment = new(2026, 11, 9, 19, 32, 0, TimeSpan.Zero);

    public FakeTimeProvider Clock { get; } = new(DesignMoment);

    public TestPrioritySymbols Priority { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<TimeProvider>(Clock);
            services.AddSingleton<IPrioritySymbolSource>(Priority);
        });
    }

    public async Task RunAsync<TJob, THandler>(TJob job)
        where TJob : IJob
        where THandler : IJobHandler<TJob>
    {
        _ = CreateClient();
        await using var scope = Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<THandler>().HandleAsync(job, new JobContext(Guid.NewGuid(), 1, 3, null), default);
    }

    public async Task<T> QueryAsync<T>(Func<MarketDataDbContext, Task<T>> query)
    {
        _ = CreateClient();
        await using var scope = Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<MarketDataDbContext>());
    }
}

/// <summary>A one-request-per-minute budget: a quote cycle can fetch a single batch.</summary>
public sealed class TightBudgetIngestionApiFactory : IngestionApiFactory
{
    protected override IReadOnlyDictionary<string, string> ExtraSettings => new Dictionary<string, string> { ["MarketData:Ingestion:RequestsPerMinute"] = "1" };
}

public class MarketIngestionTests(IngestionApiFactory factory) : IClassFixture<IngestionApiFactory>
{
    [Fact]
    public async Task Instrument_sync_loads_the_universe_once_per_day()
    {
        await factory.RunAsync<InstrumentSyncJob, InstrumentSyncHandler>(new InstrumentSyncJob());
        var firstUpdate = await factory.QueryAsync(db => db.Instruments.MaxAsync(i => i.UpdatedAt));
        await factory.RunAsync<InstrumentSyncJob, InstrumentSyncHandler>(new InstrumentSyncJob());

        (await factory.QueryAsync(db => db.Instruments.CountAsync(i => i.IsActive))).ShouldBe(1034);
        var nvda = await factory.QueryAsync(db => db.Instruments.SingleAsync(i => i.Symbol == "NVDA"));
        (nvda.ExchangeMic, nvda.Name, nvda.Sector).ShouldBe(("XNAS", "NVIDIA Corp.", "Information Technology"));
        JsonDocument.Parse(nvda.ProviderRefs).RootElement.GetProperty("fake").GetString().ShouldBe("NVDA");
        (await factory.QueryAsync(db => db.Instruments.MaxAsync(i => i.UpdatedAt))).ShouldBe(firstUpdate); // second run was not due
    }

    [Fact]
    public async Task Calendar_sync_stores_sessions_and_holidays_a_year_ahead()
    {
        await factory.RunAsync<InstrumentSyncJob, InstrumentSyncHandler>(new InstrumentSyncJob());
        await factory.RunAsync<CalendarSyncJob, CalendarSyncHandler>(new CalendarSyncJob());

        var thanksgiving = await factory.QueryAsync(db => db.TradingCalendar.SingleAsync(s => s.ExchangeMic == "XNYS" && s.SessionDate == new DateOnly(2026, 11, 26)));
        thanksgiving.IsHoliday.ShouldBeTrue();
        (await factory.QueryAsync(db => db.TradingCalendar.MaxAsync(s => s.SessionDate))).ShouldBeGreaterThanOrEqualTo(new DateOnly(2027, 11, 1));
        (await factory.QueryAsync(db => db.TradingCalendar.Select(s => s.ExchangeMic).Distinct().OrderBy(m => m).ToListAsync())).ShouldBe(["ARCX", "XNAS", "XNYS"]);
    }

    [Fact]
    public async Task Eod_sync_backfills_final_bars_once_per_closed_session()
    {
        await factory.RunAsync<InstrumentSyncJob, InstrumentSyncHandler>(new InstrumentSyncJob());
        await factory.RunAsync<EodBarSyncJob, EodBarSyncHandler>(new EodBarSyncJob());

        var nvdaBars = await DailyBarsAsync("NVDA");
        nvdaBars.Count.ShouldBeGreaterThan(300);
        nvdaBars.ShouldAllBe(b => b.IsFinal);
        nvdaBars[^1].SessionDate.ShouldBe(new DateOnly(2026, 11, 6)); // today's forming bar is not stored
        nvdaBars[^1].Close.ShouldBe(180.24m);
        (await factory.QueryAsync(db => db.PriceBars.Select(b => b.InstrumentId).Distinct().CountAsync())).ShouldBe(1034);
        var nvdaId = await factory.QueryAsync(db => db.Instruments.Where(i => i.Symbol == "NVDA").Select(i => i.Id).SingleAsync());
        (await factory.QueryAsync(db => db.CorporateActions.CountAsync(a => a.InstrumentId == nvdaId))).ShouldBe(2);

        var total = await factory.QueryAsync(db => db.PriceBars.CountAsync());
        await factory.RunAsync<EodBarSyncJob, EodBarSyncHandler>(new EodBarSyncJob());
        (await factory.QueryAsync(db => db.PriceBars.CountAsync())).ShouldBe(total); // not due again before the next close
    }

    [Fact]
    public async Task Quote_sync_upserts_quotes_and_publishes_an_event_only_when_something_changed()
    {
        await factory.RunAsync<InstrumentSyncJob, InstrumentSyncHandler>(new InstrumentSyncJob());
        var eventsBefore = await QuoteEventsAsync();

        await factory.RunAsync<QuoteSyncJob, QuoteSyncHandler>(new QuoteSyncJob());
        var nvda = await QuoteAsync("NVDA");
        (nvda.Last, nvda.PrevClose, nvda.Volume, nvda.Quality, nvda.Source).ShouldBe((186.40m, 180.24m, 412_800_000L, "synthetic", "fake"));
        (await factory.QueryAsync(db => db.Quotes.CountAsync())).ShouldBe(1034);
        var events = await QuoteEventsAsync();
        events.Count.ShouldBe(eventsBefore.Count + 1);
        JsonDocument.Parse(events[^1]).RootElement.GetProperty("quotes").GetArrayLength().ShouldBeGreaterThan(0);

        // Same moment again: nothing changed, so no new sequence number and no event.
        var seq = nvda.QuoteSeq;
        await factory.RunAsync<QuoteSyncJob, QuoteSyncHandler>(new QuoteSyncJob());
        (await QuoteAsync("NVDA")).QuoteSeq.ShouldBe(seq);
        (await QuoteEventsAsync()).Count.ShouldBe(events.Count);
    }

    [Fact]
    public async Task Quote_sync_drops_bad_ticks_and_survives_an_outage()
    {
        await factory.RunAsync<InstrumentSyncJob, InstrumentSyncHandler>(new InstrumentSyncJob());
        var faults = factory.Services.GetRequiredService<MarketDataFaults>();
        factory.Priority.Symbols.Add("AMD"); // AMD is first in the request, so the bad tick hits it
        try
        {
            faults.Set(MarketDataFaultMode.BadTick);
            await factory.RunAsync<QuoteSyncJob, QuoteSyncHandler>(new QuoteSyncJob());
            (await factory.QueryAsync(db => db.Quotes.AnyAsync(q => q.Last == 1646.60m))).ShouldBeFalse(); // 10 × 164.66 never stored
            (await QuoteAsync("CRM")).Last.ShouldBe(312.40m);

            faults.Set(MarketDataFaultMode.Down);
            await factory.RunAsync<QuoteSyncJob, QuoteSyncHandler>(new QuoteSyncJob()); // no throw: next cycle retries
            var state = await factory.QueryAsync(db => db.ProviderSyncState.SingleAsync(s => s.Provider == "fake" && s.Dataset == SyncDataset.Quotes));
            (state.LastErrorCode, state.ConsecutiveFailures).ShouldBe(("provider_unavailable", 1));

            await Should.ThrowAsync<MarketDataUnavailableException>(() => factory.RunAsync<EodBarSyncJob, EodBarSyncHandler>(new EodBarSyncJob())); // EOD wants job-queue retries
        }
        finally
        {
            faults.Set(MarketDataFaultMode.Ok);
            factory.Priority.Symbols.Clear();
        }
    }

    [Fact]
    public async Task Fundamentals_sync_covers_equities_only()
    {
        await factory.RunAsync<InstrumentSyncJob, InstrumentSyncHandler>(new InstrumentSyncJob());
        await factory.RunAsync<FundamentalsSyncJob, FundamentalsSyncHandler>(new FundamentalsSyncJob());

        var count = await factory.QueryAsync(db => db.Fundamentals.CountAsync());
        count.ShouldBe(await factory.QueryAsync(db => db.Instruments.CountAsync(i => i.AssetType == AssetType.Equity)));
        var nvda = await factory.QueryAsync(db => db.Fundamentals.SingleAsync(f => db.Instruments.Any(i => i.Id == f.InstrumentId && i.Symbol == "NVDA")));
        (nvda.Beta, nvda.NextEarningsDate).ShouldBe((1.72m, new DateOnly(2026, 11, 23)));
    }

    [Fact]
    public async Task News_sync_stores_headlines_for_priority_symbols_without_duplicates()
    {
        await factory.RunAsync<InstrumentSyncJob, InstrumentSyncHandler>(new InstrumentSyncJob());
        factory.Priority.Symbols.AddRange(["NVDA", "AMD"]);
        try
        {
            await factory.RunAsync<NewsSyncJob, NewsSyncHandler>(new NewsSyncJob());
            var articles = await factory.QueryAsync(db => db.NewsArticles.CountAsync());
            await factory.RunAsync<NewsSyncJob, NewsSyncHandler>(new NewsSyncJob());

            (await factory.QueryAsync(db => db.NewsArticles.CountAsync())).ShouldBe(articles);
            var shared = await factory.QueryAsync(db => db.NewsArticles.SingleAsync(a => a.ProviderArticleId == "fx-002"));
            (await factory.QueryAsync(db => db.InstrumentNews.CountAsync(n => n.ArticleId == shared.Id))).ShouldBe(2); // NVDA and AMD
        }
        finally
        {
            factory.Priority.Symbols.Clear();
        }
    }

    private Task<List<PriceBar>> DailyBarsAsync(string symbol) => IngestionQueries.DailyBarsAsync(factory, symbol);

    private Task<Quote> QuoteAsync(string symbol) => IngestionQueries.QuoteAsync(factory, symbol);

    private Task<List<string>> QuoteEventsAsync() => IngestionQueries.QuoteEventsAsync(factory);
}

/// <summary>The clock only moves forward, so the time-dependent steps run in order in their own host.</summary>
public sealed class IngestionTimelineApiFactory : IngestionApiFactory;

public class MarketIngestionTimelineTests(IngestionTimelineApiFactory factory) : IClassFixture<IngestionTimelineApiFactory>
{
    [Fact]
    public async Task Quotes_advance_through_the_session_and_the_close_adds_one_final_bar_per_instrument()
    {
        await factory.RunAsync<InstrumentSyncJob, InstrumentSyncHandler>(new InstrumentSyncJob());
        await factory.RunAsync<QuoteSyncJob, QuoteSyncHandler>(new QuoteSyncJob());
        var seq = (await IngestionQueries.QuoteAsync(factory, "NVDA")).QuoteSeq;
        var events = (await IngestionQueries.QuoteEventsAsync(factory)).Count;

        // A minute later the session is still open: asOf moves on, so every quote changes.
        factory.Clock.Advance(TimeSpan.FromMinutes(1));
        await factory.RunAsync<QuoteSyncJob, QuoteSyncHandler>(new QuoteSyncJob());
        var later = await IngestionQueries.QuoteAsync(factory, "NVDA");
        later.QuoteSeq.ShouldBe(seq + 1);
        later.AsOf.ShouldBe(IngestionApiFactory.DesignMoment.AddMinutes(1));
        (await IngestionQueries.QuoteEventsAsync(factory)).Count.ShouldBe(events + 1);

        // Backfill at 14:33, then Monday's bar arrives once the close is 30 minutes old.
        await factory.RunAsync<EodBarSyncJob, EodBarSyncHandler>(new EodBarSyncJob());
        var total = await factory.QueryAsync(db => db.PriceBars.CountAsync());
        factory.Clock.SetUtcNow(new DateTimeOffset(2026, 11, 9, 21, 31, 0, TimeSpan.Zero)); // 16:31 ET
        await factory.RunAsync<EodBarSyncJob, EodBarSyncHandler>(new EodBarSyncJob());
        (await factory.QueryAsync(db => db.PriceBars.CountAsync())).ShouldBe(total + 1034);
        var monday = (await IngestionQueries.DailyBarsAsync(factory, "NVDA"))[^1];
        (monday.SessionDate, monday.Close, monday.Volume, monday.IsFinal).ShouldBe((new DateOnly(2026, 11, 9), 186.40m, 412_800_000L, true));

        // After the close, one more quote sync captures closing values; then quotes rest until the next session.
        await factory.RunAsync<QuoteSyncJob, QuoteSyncHandler>(new QuoteSyncJob());
        (await IngestionQueries.QuoteAsync(factory, "NVDA")).AsOf.ShouldBe(new DateTimeOffset(2026, 11, 9, 21, 0, 0, TimeSpan.Zero));
        var closingSeq = (await IngestionQueries.QuoteAsync(factory, "NVDA")).QuoteSeq;
        factory.Clock.Advance(TimeSpan.FromMinutes(5));
        await factory.RunAsync<QuoteSyncJob, QuoteSyncHandler>(new QuoteSyncJob());
        (await IngestionQueries.QuoteAsync(factory, "NVDA")).QuoteSeq.ShouldBe(closingSeq);
    }
}

/// <summary>A host whose market tables stay empty until a test syncs them.</summary>
public sealed class EmptyMarketApiFactory : IngestionApiFactory;

public class MarketIngestionStartupTests(EmptyMarketApiFactory factory) : IClassFixture<EmptyMarketApiFactory>
{
    [Fact]
    public async Task Jobs_that_run_before_the_first_instrument_sync_wait_for_it()
    {
        // The recurring schedules all fire at startup in any order; on an empty database EOD and fundamentals must not
        // record a successful (empty) run, or the backfill would wait for the next close.
        await factory.RunAsync<EodBarSyncJob, EodBarSyncHandler>(new EodBarSyncJob());
        await factory.RunAsync<FundamentalsSyncJob, FundamentalsSyncHandler>(new FundamentalsSyncJob());
        (await factory.QueryAsync(db => db.ProviderSyncState.CountAsync(s => s.Dataset == SyncDataset.EodBars || s.Dataset == SyncDataset.Fundamentals))).ShouldBe(0);
    }
}

internal static class IngestionQueries
{
    public static Task<List<PriceBar>> DailyBarsAsync(IngestionApiFactory factory, string symbol) =>
        factory.QueryAsync(db => db.PriceBars
            .Where(b => b.Timeframe == Timeframe.OneDay && db.Instruments.Any(i => i.Id == b.InstrumentId && i.Symbol == symbol))
            .OrderBy(b => b.SessionDate)
            .ToListAsync());

    public static Task<Quote> QuoteAsync(IngestionApiFactory factory, string symbol) =>
        factory.QueryAsync(db => db.Quotes.AsNoTracking().SingleAsync(q => db.Instruments.Any(i => i.Id == q.InstrumentId && i.Symbol == symbol)));

    public static async Task<List<string>> QuoteEventsAsync(IngestionApiFactory factory)
    {
        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT payload::text FROM infra.outbox_messages WHERE type = 'market.quotes_updated.v1' ORDER BY occurred_at", connection);
        var payloads = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            payloads.Add(reader.GetString(0));
        }

        return payloads;
    }
}

public class QuotePriorityTests(TightBudgetIngestionApiFactory factory) : IClassFixture<TightBudgetIngestionApiFactory>
{
    [Fact]
    public async Task Priority_symbols_get_quotes_first_when_the_budget_is_short()
    {
        await factory.RunAsync<InstrumentSyncJob, InstrumentSyncHandler>(new InstrumentSyncJob()); // spends the one token
        factory.Clock.Advance(TimeSpan.FromMinutes(1)); // refills it
        factory.Priority.Symbols.AddRange(["ZZ900", "LLY"]);

        await factory.RunAsync<QuoteSyncJob, QuoteSyncHandler>(new QuoteSyncJob());

        var symbols = await factory.QueryAsync(db => db.Quotes.Join(db.Instruments, q => q.InstrumentId, i => i.Id, (q, i) => i.Symbol).ToListAsync());
        symbols.Count.ShouldBe(200); // one batch
        symbols.ShouldContain("ZZ900");
        symbols.ShouldContain("LLY");
        var state = await factory.QueryAsync(db => db.ProviderSyncState.SingleAsync(s => s.Dataset == SyncDataset.Quotes));
        state.LastErrorCode.ShouldBe("rate_budget");
    }
}
