using Astryx.Infrastructure.Jobs;
using Astryx.Infrastructure.Persistence;
using Astryx.Modules.Invest.Api;
using Astryx.Modules.Invest.Application;
using Astryx.Modules.Invest.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Astryx.Modules.Invest;

/// <summary>Composition entry point for the Invest module (TASKS.md T-164).</summary>
public static class InvestModule
{
    public static IServiceCollection AddInvestModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<InvestDbContext>(InvestDbContext.SchemaName, historyKey: "invest");
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<InvestService>();
        services.AddJobHandler<ContributionJob, ContributionHandler>();
        services.AddRecurringJob("invest-contributions", TimeSpan.FromHours(1), _ => new ContributionJob());
        services.AddValidatorsFromAssemblyContaining<SaveInvestPlanRequestValidator>(includeInternalTypes: true);
        return services;
    }
}
