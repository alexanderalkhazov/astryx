using System.Net.Http.Json;
using System.Text.Json;
using Astryx.Contracts.Portfolio;
using Astryx.Modules.Portfolio.Application;
using Astryx.Modules.Portfolio.Domain;
using Astryx.Modules.Portfolio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Astryx.IntegrationTests;

/// <summary>The append-only cash ledger and its invariants (SYSTEM_DESIGN.md §10.4, §15.3, §20.8, TASKS.md T-080).</summary>
public class PortfolioLedgerTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private AsyncServiceScope Scope() => factory.Services.CreateAsyncScope();

    /// <summary>A new user's $100,000.00 paper portfolio, provisioned by registration (its opening deposit included).</summary>
    private async Task<Guid> NewPortfolioAsync(decimal reserved = 0m)
    {
        var client = await TestUsers.SignedInClientAsync(factory);
        var userId = (await client.GetFromJsonAsync<JsonElement>("/api/v1/me")).GetProperty("id").GetGuid();
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (true)
        {
            await using var scope = Scope();
            var db = scope.ServiceProvider.GetRequiredService<PortfolioDbContext>();
            var id = await db.Portfolios.IgnoreQueryFilters().Where(p => p.UserId == userId).Select(p => (Guid?)p.Id).SingleOrDefaultAsync();
            if (id is { } portfolioId && await db.Transactions.IgnoreQueryFilters().AnyAsync(t => t.PortfolioId == portfolioId))
            {
                if (reserved > 0)
                {
                    await db.Portfolios.IgnoreQueryFilters().Where(p => p.Id == portfolioId).ExecuteUpdateAsync(s => s.SetProperty(p => p.ReservedCash, reserved));
                }

                return portfolioId;
            }

            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("The paper portfolio was not provisioned.");
            }

            await Task.Delay(100);
        }
    }

    private async Task<Astryx.SharedKernel.Results.Result<LedgerPosting>> PostAsync(Guid portfolioId, params LedgerEntry[] entries)
    {
        await using var scope = Scope();
        return await scope.ServiceProvider.GetRequiredService<IPortfolioLedger>().PostAsync(portfolioId, entries, default);
    }

    private async Task<IReadOnlyList<string>> InvariantsAsync(Guid portfolioId)
    {
        await using var scope = Scope();
        return await LedgerInvariants.CheckAsync(scope.ServiceProvider.GetRequiredService<PortfolioDbContext>(), portfolioId);
    }

    private static LedgerEntry[] DesignFill(Guid fillId) =>
    [
        new(LedgerType.Buy, -14_175.52m, $"fill:{fillId}:principal", "Buy 76 NVDA @ 186.52", FillId: fillId),
        new(LedgerType.Fee, -0.01m, $"fill:{fillId}:fee", "Commission", FillId: fillId),
    ];

    [Fact]
    public async Task A_fill_posts_principal_and_fee_atomically_with_a_running_balance()
    {
        var portfolioId = await NewPortfolioAsync();
        var fill = Guid.NewGuid();

        var result = await PostAsync(portfolioId, DesignFill(fill));

        result.IsSuccess.ShouldBeTrue();
        (result.Value.CashBalance, result.Value.Version, result.Value.Replayed, result.Value.TransactionIds.Count).ShouldBe((85_824.47m, 2L, false, 2));
        await using var scope = Scope();
        var rows = await scope.ServiceProvider.GetRequiredService<PortfolioDbContext>().Transactions.IgnoreQueryFilters()
            .Where(t => t.PortfolioId == portfolioId).OrderBy(t => t.Seq).ToListAsync();
        rows.Select(r => (r.Type, r.BalanceAfter)).ShouldBe([("initial_deposit", 100_000m), ("buy", 85_824.48m), ("fee", 85_824.47m)]);
        rows[1].FillId.ShouldBe(fill);
        (await InvariantsAsync(portfolioId)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Reposting_the_same_references_is_a_replay_and_changes_nothing()
    {
        var portfolioId = await NewPortfolioAsync();
        var fill = Guid.NewGuid();
        var first = await PostAsync(portfolioId, DesignFill(fill));

        var again = await PostAsync(portfolioId, DesignFill(fill));

        again.Value.Replayed.ShouldBeTrue();
        again.Value.TransactionIds.ShouldBe(first.Value.TransactionIds);
        (again.Value.CashBalance, again.Value.Version).ShouldBe((first.Value.CashBalance, first.Value.Version));
        (await PostAsync(portfolioId, DesignFill(fill)[0], new LedgerEntry(LedgerType.Fee, -1m, $"fill:{Guid.NewGuid()}:fee"))).Error!.Code.ShouldBe("conflict");
        (await InvariantsAsync(portfolioId)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Cash_can_never_drop_below_what_working_orders_reserved()
    {
        var portfolioId = await NewPortfolioAsync(reserved: 90_000m);

        var result = await PostAsync(portfolioId, new LedgerEntry(LedgerType.Buy, -10_000.01m, $"fill:{Guid.NewGuid()}:principal"));

        result.Error!.Code.ShouldBe("insufficient_funds");
        await using var scope = Scope();
        var db = scope.ServiceProvider.GetRequiredService<PortfolioDbContext>();
        (await db.Transactions.IgnoreQueryFilters().CountAsync(t => t.PortfolioId == portfolioId)).ShouldBe(1);
        (await db.Portfolios.IgnoreQueryFilters().SingleAsync(p => p.Id == portfolioId)).Version.ShouldBe(1);
    }

    [Theory]
    [InlineData("buy", "-10.005", "a")]
    [InlineData("buy", "0", "a")]
    [InlineData("initial_deposit", "100", "a")]
    [InlineData("buy", "-1", "")]
    public async Task Malformed_entries_are_rejected(string type, string amount, string reference)
    {
        var portfolioId = await NewPortfolioAsync();

        var result = await PostAsync(portfolioId, new LedgerEntry(type, decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture), reference));

        result.Error!.Code.ShouldBe("validation_failed");
    }

    [Fact]
    public async Task Concurrent_postings_serialise_on_the_portfolio_row()
    {
        var portfolioId = await NewPortfolioAsync();

        var results = await Task.WhenAll(Enumerable.Range(0, 12).Select(i => PostAsync(portfolioId, new LedgerEntry(LedgerType.Fee, -1.25m, $"concurrent:{portfolioId}:{i}"))));

        results.ShouldAllBe(r => r.IsSuccess);
        results.Select(r => r.Value.Version).Order().ShouldBe(Enumerable.Range(2, 12).Select(v => (long)v));
        results.Min(r => r.Value.CashBalance).ShouldBe(99_985m);
        (await InvariantsAsync(portfolioId)).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("UPDATE portfolio.transactions SET amount = 1")]
    [InlineData("DELETE FROM portfolio.transactions")]
    [InlineData("TRUNCATE portfolio.transactions CASCADE")]
    public async Task The_ledger_table_is_append_only(string sql)
    {
        await NewPortfolioAsync();
        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);

        var error = await Should.ThrowAsync<PostgresException>(() => command.ExecuteNonQueryAsync());

        error.MessageText.ShouldContain("append-only");
    }

    [Fact]
    public async Task One_open_position_per_instrument()
    {
        var portfolioId = await NewPortfolioAsync();
        var instrument = Guid.NewGuid();
        Position Open() => new() { Id = Guid.NewGuid(), PortfolioId = portfolioId, InstrumentId = instrument, Symbol = "NVDA", Quantity = 76, AvgEntryPrice = 186.52m, CostBasis = 14_175.52m, OpenedAt = DateTimeOffset.UtcNow, Version = 1 };
        await using (var scope = Scope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortfolioDbContext>();
            db.Positions.Add(Open());
            await db.SaveChangesAsync();
        }

        await using var second = Scope();
        var db2 = second.ServiceProvider.GetRequiredService<PortfolioDbContext>();
        db2.Positions.Add(Open());
        (await Should.ThrowAsync<DbUpdateException>(() => db2.SaveChangesAsync())).InnerException.ShouldBeOfType<PostgresException>().ConstraintName.ShouldBe("ux_positions_one_open");
    }
}
