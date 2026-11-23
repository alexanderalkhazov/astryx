using System.Reflection;
using Astryx.Infrastructure.Security;

namespace Astryx.ArchitectureTests;

/// <summary>
/// Every persisted type that stores a user id must be filtered per user (SYSTEM_DESIGN.md §18.5, TASKS.md T-024).
/// A new table with a <c>Guid UserId</c> fails this test until it implements <see cref="IUserOwned"/> or is added to
/// <see cref="Exempt"/> with a reason.
/// </summary>
public class UserOwnershipTests
{
    /// <summary>Types allowed to carry a user id without the per-user filter, and why.</summary>
    private static readonly Dictionary<string, string> Exempt = new(StringComparer.Ordinal)
    {
        // (none yet) e.g. ["Astryx.Modules.X.Domain.Y"] = "admin-only, never exposed to users",
    };

    public static TheoryData<string> ModuleAssemblies => new()
    {
        typeof(Astryx.Modules.Identity.IdentityModule).Assembly.GetName().Name!,
        typeof(Astryx.Modules.Profiles.ProfilesModule).Assembly.GetName().Name!,
        typeof(Astryx.Modules.Watchlists.WatchlistsModule).Assembly.GetName().Name!,
        typeof(Astryx.Modules.Invest.InvestModule).Assembly.GetName().Name!,
        typeof(Astryx.Modules.Portfolio.PortfolioModule).Assembly.GetName().Name!,
        typeof(Astryx.Modules.Audit.AuditModule).Assembly.GetName().Name!,
        typeof(Astryx.Modules.Notifications.NotificationsModule).Assembly.GetName().Name!,
        typeof(Astryx.Modules.Trading.TradingModule).Assembly.GetName().Name!,
        typeof(Astryx.Modules.Paper.PaperModule).Assembly.GetName().Name!,
        typeof(Astryx.Modules.Ai.AiModule).Assembly.GetName().Name!,
        typeof(Astryx.Modules.Discovery.DiscoveryModule).Assembly.GetName().Name!,
        typeof(Astryx.Modules.MarketData.MarketDataModule).Assembly.GetName().Name!,
        typeof(Astryx.Modules.Strategies.StrategiesModule).Assembly.GetName().Name!,
        typeof(Astryx.Modules.Quant.QuantModule).Assembly.GetName().Name!,
    };

    [Theory]
    [MemberData(nameof(ModuleAssemblies))]
    public void Domain_types_with_a_user_id_are_user_owned(string assemblyName)
    {
        var offenders = Assembly.Load(assemblyName).GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && t.Namespace?.EndsWith(".Domain", StringComparison.Ordinal) == true)
            .Where(t => t.GetProperty("UserId")?.PropertyType == typeof(Guid))
            .Where(t => !typeof(IUserOwned).IsAssignableFrom(t) && !Exempt.ContainsKey(t.FullName!))
            .Select(t => t.FullName)
            .ToList();

        offenders.ShouldBeEmpty();
    }
}
