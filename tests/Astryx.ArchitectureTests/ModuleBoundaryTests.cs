using System.Reflection;

namespace Astryx.ArchitectureTests;

/// <summary>Modules depend only on the building blocks (SharedKernel, Contracts, Infrastructure), never on each other or on Workers (SYSTEM_DESIGN.md §4.4).</summary>
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
    public void Module_does_not_reference_workers_or_api(string moduleName)
    {
        var references = Assembly.Load(moduleName).GetReferencedAssemblies().Select(a => a.Name!).ToList();

        references.ShouldNotContain("Astryx.Workers");
        references.ShouldNotContain("Astryx.Api");
    }

    [Fact]
    public void Building_blocks_do_not_reference_modules()
    {
        var infrastructure = typeof(Astryx.Infrastructure.StartupGuards).Assembly.GetReferencedAssemblies().Select(a => a.Name!);

        infrastructure.ShouldNotContain(n => n.StartsWith("Astryx.Modules.", StringComparison.Ordinal));
    }
}
