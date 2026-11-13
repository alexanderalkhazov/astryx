using System.Globalization;
using Astryx.Contracts.Events;
using Astryx.Contracts.Events.Identity;
using Astryx.Modules.Portfolio.Domain;
using Astryx.Modules.Portfolio.Infrastructure.Persistence;
using Astryx.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using PortfolioEntity = Astryx.Modules.Portfolio.Domain.Portfolio;

namespace Astryx.Modules.Portfolio.Application;

/// <summary>
/// Opens the simulated paper portfolio for a new user with an <c>initial_deposit</c> of <c>Paper:StartingCash</c>
/// ($100,000.00 by default, A-4). Idempotent: one portfolio per user and mode, one deposit per portfolio.
/// </summary>
public sealed class CreatePaperPortfolioHandler(PortfolioDbContext db, IConfiguration configuration, TimeProvider timeProvider)
    : IIntegrationEventHandler<UserRegistered>
{
    public async Task HandleAsync(UserRegistered integrationEvent, IntegrationEventContext context, CancellationToken cancellationToken)
    {
        if (await db.Portfolios.AnyAsync(p => p.UserId == integrationEvent.UserId && p.Mode == PortfolioMode.Simulated, cancellationToken))
        {
            return;
        }

        var startingCash = decimal.Parse(configuration["Paper:StartingCash"] ?? "100000.00", NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
        var now = timeProvider.GetUtcNow();
        var portfolio = new PortfolioEntity
        {
            Id = Ids.New(timeProvider),
            UserId = integrationEvent.UserId,
            Mode = PortfolioMode.Simulated,
            StartingCash = startingCash,
            CashBalance = startingCash,
            Version = 1,
            CreatedAt = now,
            UpdatedAt = now,
            EpochStartedAt = now,
        };
        db.Portfolios.Add(portfolio);
        db.Transactions.Add(new LedgerTransaction
        {
            Id = Ids.New(timeProvider),
            PortfolioId = portfolio.Id,
            Type = LedgerType.InitialDeposit,
            Amount = startingCash,
            BalanceAfter = startingCash,
            IdempotencyRef = $"initial_deposit:{portfolio.Id}",
            OccurredAt = now,
            Description = "Paper account opening balance",
        });
        await db.SaveChangesAsync(cancellationToken);
    }
}
