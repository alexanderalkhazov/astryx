using Astryx.Infrastructure.Persistence;
using Astryx.Modules.Paper.Application;
using Astryx.Modules.Paper.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Astryx.Modules.Paper;

/// <summary>Composition entry point for the Paper module (SYSTEM_DESIGN.md §7.11, §15).</summary>
public static class PaperModule
{
    public static IServiceCollection AddPaperModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<PaperDbContext>(PaperDbContext.SchemaName, historyKey: "paper");
        services.AddScoped<PaperOrderService>();
        services.AddScoped<Astryx.Contracts.Paper.IOpenOrders, OpenOrders>();
        return services;
    }
}
