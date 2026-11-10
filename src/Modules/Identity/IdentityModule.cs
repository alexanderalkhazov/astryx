using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Astryx.Modules.Identity;

/// <summary>Composition entry point for the Identity module (SYSTEM_DESIGN.md §7). NOT IMPLEMENTED.</summary>
public static class IdentityModule
{
    public static IServiceCollection AddIdentityModule(this IServiceCollection services, IConfiguration configuration) => services;
}
