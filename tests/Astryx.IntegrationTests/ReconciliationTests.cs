using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Astryx.Infrastructure.Jobs;
using Astryx.Modules.Audit.Infrastructure.Persistence;
using Astryx.Modules.Paper.Infrastructure.Persistence;
using Astryx.Modules.Portfolio.Application;
using Astryx.Modules.Portfolio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Astryx.IntegrationTests;

public abstract class ReconciliationScenario(TradingApiFactory factory) : IAsyncLifetime
{
    protected TradingApiFactory Factory => factory;

    public async Task InitializeAsync()
    {
        await factory.PrepareAsync();
        await factory.ScanAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    protected async Task<(HttpClient Client, Guid PortfolioId, JsonElement Order)> PlaceAsync(decimal last)
    {
        var client = await TestUsers.SignedInClientAsync(factory);
        await factory.MakeQuoteLiveAsync("NVDA", last);
        var proposal = await (await client.PostAsJsonAsync("/api/v1/proposals", new { source = "signal", signalId = await factory.ActiveSignalIdAsync("NVDA") })).Content.ReadFromJsonAsync<JsonElement>();
        var response = await SendAsync(client, "/api/v1/paper/orders", new { proposalId = proposal.GetProperty("id").GetGuid(), assessmentId = proposal.GetProperty("assessment").GetProperty("id").GetGuid() });
        var order = await response.Content.ReadFromJsonAsync<JsonElement>();
        var portfolioId = (await client.GetFromJsonAsync<JsonElement>("/api/v1/portfolio")).GetProperty("id").GetGuid();
        return (client, portfolioId, order);
    }

    protected static Task<HttpResponseMessage> SendAsync(HttpClient client, string path, object? body = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body ?? new { }) };
        request.Headers.Add("Idempotency-Key", $"key-{Guid.NewGuid():N}");
        return client.SendAsync(request);
    }

    protected async Task<IReadOnlyList<string>> ReconcileAsync(Guid portfolioId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<PortfolioReconciler>().ReconcileAsync(portfolioId, default);
    }

    protected async Task<T> Query<TContext, T>(Func<TContext, Task<T>> query)
        where TContext : notnull
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<TContext>());
    }

    protected async Task SqlAsync<TContext>(FormattableString sql)
        where TContext : DbContext
    {
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<TContext>().Database.ExecuteSqlAsync(sql);
    }
}

/// <summary>Nightly portfolio reconciliation, checks 1–6 (SYSTEM_DESIGN.md §20.13, TASKS.md T-100).</summary>
public class ReconciliationTests(TradingApiFactory factory) : ReconciliationScenario(factory), IClassFixture<TradingApiFactory>
{
    [Fact]
    public async Task A_cash_mismatch_halts_new_entries_but_exits_still_work_and_is_audited()
    {
        var (client, portfolioId, order) = await PlaceAsync(186.60m);
        await SqlAsync<PortfolioDbContext>($"UPDATE portfolio.portfolios SET cash_balance = cash_balance + 1 WHERE id = {portfolioId}");

        var issues = await ReconcileAsync(portfolioId);

        issues.ShouldContain(i => i.StartsWith("ledger: cash_balance"));
        var summary = await client.GetFromJsonAsync<JsonElement>("/api/v1/portfolio");
        (summary.GetProperty("halted").GetBoolean(), summary.GetProperty("haltReason").GetString()).ShouldBe((true, "reconciliation"));
        var attention = (await client.GetFromJsonAsync<JsonElement>("/api/v1/portfolio/attention")).GetProperty("items").EnumerateArray();
        attention.ShouldContain(a => a.GetProperty("kind").GetString() == "halted");
        var userId = (await client.GetFromJsonAsync<JsonElement>("/api/v1/me")).GetProperty("id").GetGuid();
        (await Query<AuditDbContext, bool>(db => db.AuditLogs.AnyAsync(l => l.UserId == userId && l.Action == "portfolio.reconciliation_failed"))).ShouldBeTrue();

        // New entries: refused at the execution gate.
        var other = await (await client.PostAsJsonAsync("/api/v1/proposals", new { source = "signal", signalId = await Factory.ActiveSignalIdAsync("NVDA") })).Content.ReadFromJsonAsync<JsonElement>();
        if (other.TryGetProperty("assessment", out var assessment))
        {
            var refused = await SendAsync(client, "/api/v1/paper/orders", new { proposalId = other.GetProperty("id").GetGuid(), assessmentId = assessment.GetProperty("id").GetGuid() });
            refused.StatusCode.ShouldBe(HttpStatusCode.Conflict);
            (await refused.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().ShouldBeOneOf("portfolio_halted", "risk_check_failed");
        }

        // Exits: still allowed.
        (await SendAsync(client, $"/api/v1/positions/{order.GetProperty("positionId").GetGuid()}/close")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await client.GetFromJsonAsync<JsonElement>("/api/v1/portfolio")).GetProperty("haltReason").GetString().ShouldBe("reconciliation"); // never cleared automatically
    }

    [Fact]
    public async Task A_halted_portfolio_refuses_a_new_entry_with_portfolio_halted()
    {
        var client = await TestUsers.SignedInClientAsync(Factory);
        await Factory.MakeQuoteLiveAsync("NVDA", 186.60m);
        var proposal = await (await client.PostAsJsonAsync("/api/v1/proposals", new { source = "signal", signalId = await Factory.ActiveSignalIdAsync("NVDA") })).Content.ReadFromJsonAsync<JsonElement>();
        var portfolioId = (await client.GetFromJsonAsync<JsonElement>("/api/v1/portfolio")).GetProperty("id").GetGuid();
        await SqlAsync<PortfolioDbContext>($"UPDATE portfolio.portfolios SET reconciliation_halted_at = now() WHERE id = {portfolioId}");

        var response = await SendAsync(client, "/api/v1/paper/orders", new { proposalId = proposal.GetProperty("id").GetGuid(), assessmentId = proposal.GetProperty("assessment").GetProperty("id").GetGuid() });

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().ShouldBe("portfolio_halted");
        (await Query<PaperDbContext, int>(db => db.Orders.IgnoreQueryFilters().CountAsync(o => o.PortfolioId == portfolioId))).ShouldBe(0);
    }

    [Fact]
    public async Task Missing_exits_overdue_orders_and_bad_realized_pnl_are_each_reported()
    {
        var (_, open, order) = await PlaceAsync(186.60m);
        var positionId = order.GetProperty("positionId").GetGuid();
        await SqlAsync<PaperDbContext>($"UPDATE trading.orders SET status = 'cancelled' WHERE position_id = {positionId} AND purpose = 'exit_stop'");
        (await ReconcileAsync(open)).ShouldContain(i => i.Contains("missing a working stop exit"));

        var (_, overdue, entry) = await PlaceAsync(185.00m);
        var entryId = entry.GetProperty("id").GetGuid();
        await SqlAsync<PaperDbContext>($"UPDATE trading.orders SET expires_at = now() - interval '1 hour' WHERE id = {entryId}");
        (await ReconcileAsync(overdue)).ShouldContain($"order {entryId}: still working after its expiry");

        var (closer, closed, toClose) = await PlaceAsync(186.60m);
        var closedPosition = toClose.GetProperty("positionId").GetGuid();
        (await SendAsync(closer, $"/api/v1/positions/{closedPosition}/close")).StatusCode.ShouldBe(HttpStatusCode.OK);
        await SqlAsync<PortfolioDbContext>($"UPDATE portfolio.positions SET realized_pnl = realized_pnl + 5 WHERE id = {closedPosition}");
        var issues = await ReconcileAsync(closed);
        issues.ShouldContain(i => i.Contains($"{closedPosition}: realized"));
        issues.ShouldContain(i => i.StartsWith("portfolio realized_pnl"));
    }

    [Fact]
    public async Task The_job_waits_for_the_session_to_close_and_then_checks_each_portfolio_once()
    {
        var (_, portfolioId, _) = await PlaceAsync(186.60m);
        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<ReconciliationHandler>().HandleAsync(new ReconciliationJob(), new JobContext(Guid.NewGuid(), 1, 1, null), default);
        }

        (await Query<PortfolioDbContext, DateTimeOffset?>(db => db.Portfolios.IgnoreQueryFilters().Where(p => p.Id == portfolioId).Select(p => p.ReconciledAt).SingleAsync())).ShouldBeNull();

        Factory.Session.CloseIn = null; // no session today
        try
        {
            await using var scope = Factory.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<ReconciliationHandler>().HandleAsync(new ReconciliationJob(), new JobContext(Guid.NewGuid(), 1, 1, null), default);
        }
        finally
        {
            Factory.Session.CloseIn = TimeSpan.FromHours(3);
        }

        (await Query<PortfolioDbContext, DateTimeOffset?>(db => db.Portfolios.IgnoreQueryFilters().Where(p => p.Id == portfolioId).Select(p => p.ReconciledAt).SingleAsync())).ShouldNotBeNull();
    }
}

/// <summary>Its own database: every portfolio in it comes from ordinary activity, so all of them must reconcile.</summary>
public class ReconciliationCleanTests(TradingApiFactory factory) : ReconciliationScenario(factory), IClassFixture<TradingApiFactory>
{
    [Fact]
    public async Task Fills_closes_resets_working_orders_and_snapshots_all_reconcile_clean()
    {
        var (_, open, _) = await PlaceAsync(186.60m);                       // an open position with its OCO exits
        var (closer, closed, order) = await PlaceAsync(186.60m);
        (await SendAsync(closer, $"/api/v1/positions/{order.GetProperty("positionId").GetGuid()}/close")).StatusCode.ShouldBe(HttpStatusCode.OK);
        var (resetter, reset, _) = await PlaceAsync(186.60m);
        await Factory.MakeQuoteLiveAsync("NVDA", 190.00m);
        (await SendAsync(resetter, "/api/v1/portfolio/reset", new { confirm = "RESET" })).StatusCode.ShouldBe(HttpStatusCode.OK);
        var (_, working, _) = await PlaceAsync(185.00m);                     // a working buy stop
        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<PortfolioSnapshotHandler>().HandleAsync(new PortfolioSnapshotJob(), new JobContext(Guid.NewGuid(), 1, 1, null), default);
        }

        foreach (var id in new[] { open, closed, reset, working })
        {
            (await ReconcileAsync(id)).ShouldBeEmpty();
        }

        // And everything else this fixture's tests created.
        var all = await Query<PortfolioDbContext, List<Guid>>(db => db.Portfolios.IgnoreQueryFilters().Select(p => p.Id).ToListAsync());
        foreach (var id in all)
        {
            (await ReconcileAsync(id)).ShouldBeEmpty();
        }

        (await Query<PortfolioDbContext, bool>(db => db.Portfolios.IgnoreQueryFilters().AnyAsync(p => p.ReconciliationHaltedAt != null))).ShouldBeFalse();
    }
}
