using Astryx.Infrastructure.Jobs;
using Astryx.Infrastructure.Persistence;
using Astryx.Modules.Trading.Api;
using Astryx.Modules.Trading.Application;
using Astryx.Modules.Trading.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Astryx.Modules.Trading;

/// <summary>Composition entry point for the Trading module (SYSTEM_DESIGN.md §7.9).</summary>
public static class TradingModule
{
    public static IServiceCollection AddTradingModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<TradingDbContext>(TradingDbContext.SchemaName, historyKey: "trading");
        services.AddScoped<ProposalService>();
        services.AddScoped<AssessmentService>();
        services.AddScoped<Astryx.Contracts.Trading.IExecutionGate, ExecutionGate>();
        services.AddValidatorsFromAssemblyContaining<CreateProposalRequestValidator>(includeInternalTypes: true);
        services.AddJobHandler<ProposalExpiryJob, ProposalExpiryHandler>();
        services.AddRecurringJob("proposal-expiry", TimeSpan.FromMinutes(1), _ => new ProposalExpiryJob());
        return services;
    }
}
