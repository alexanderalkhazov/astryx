using System.Reflection;

namespace Astryx.ArchitectureTests;

/// <summary>Modules may only depend on SharedKernel and Contracts, never on each other (SYSTEM_DESIGN.md §4.4).</summary>
public class ModuleBoundaryTests
{
    public static TheoryData<string> Modules => new()
    {
        typeof(Astryx.Modules.Identity.IdentityModule).Assembly.GetName().Name!,
        typeof(Astryx.Modules.Profiles.ProfilesModule).Assembly.GetName().Name!,
        typeof(Astryx.Modules.MarketData.MarketDataModule).Assembly.GetName().Name!,
        typeof(Astryx.Modules.Discovery.DiscoveryModule).Assembly.GetName().Name!,
        typeof(Astryx.Modules.Watchlists.WatchlistsModule).Assembly.GetName().Name!,
        typeof(Astryx.Modules.Quant.QuantModule).Assembly.GetName().Name!,
        typeof(Astryx.Modules.Ai.AiModule).Assembly.GetName().Name!,
        typeof(Astryx.Modules.Strategies.StrategiesModule).Assembly.GetName().Name!,
        typeof(Astryx.Modules.Trading.TradingModule).Assembly.GetName().Name!,
        typeof(Astryx.Modules.Paper.PaperModule).Assembly.GetName().Name!,
        typeof(Astryx.Modules.Portfolio.PortfolioModule).Assembly.GetName().Name!,
        typeof(Astryx.Modules.Notifications.NotificationsModule).Assembly.GetName().Name!,
        typeof(Astryx.Modules.Audit.AuditModule).Assembly.GetName().Name!,
    };

    [Theory]
    [MemberData(nameof(Modules))]
    public void Module_does_not_reference_other_modules(string moduleName)
    {
        var offending = Assembly.Load(moduleName).GetReferencedAssemblies()
            .Select(a => a.Name!)
            .Where(n => n.StartsWith("Astryx.Modules.", StringComparison.Ordinal) && n != moduleName)
            .ToList();

        offending.ShouldBeEmpty();
    }

    [Theory]
    [MemberData(nameof(Modules))]
    public void Module_does_not_reference_infrastructure_or_workers(string moduleName)
    {
        var references = Assembly.Load(moduleName).GetReferencedAssemblies().Select(a => a.Name!).ToList();

        references.ShouldNotContain("Astryx.Infrastructure");
        references.ShouldNotContain("Astryx.Workers");
    }
}
