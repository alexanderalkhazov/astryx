using Astryx.Infrastructure.Outbox;
using Astryx.Infrastructure.Persistence;
using Astryx.Modules.Identity.Api;
using Astryx.Modules.Identity.Application;
using Astryx.Modules.Identity.Domain;
using Astryx.Modules.Identity.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Astryx.Modules.Identity;

/// <summary>Composition entry point for the Identity module (SYSTEM_DESIGN.md §7.1).</summary>
public static class IdentityModule
{
    public static IServiceCollection AddIdentityModule(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection("Identity");
        services.AddOptions<AccountOptions>().Bind(section);
        var iterations = section.GetValue("PasswordHashIterations", new AccountOptions().PasswordHashIterations);
        services.Configure<PasswordHasherOptions>(o =>
        {
            o.CompatibilityMode = PasswordHasherCompatibilityMode.IdentityV3;
            o.IterationCount = iterations;
        });
        services.AddSingleton<IPasswordHasher<UserAccount>, PasswordHasher<UserAccount>>();

        // Order 5: other modules' tables reference identity.users, so this schema migrates first.
        services.AddModuleDbContext<IdentityDbContext>(IdentityDbContext.SchemaName, historyKey: "identity", order: 5);
        services.AddScoped<RegistrationService>();
        services.AddValidatorsFromAssemblyContaining<RegisterRequestValidator>(includeInternalTypes: true);
        services.AddIntegrationEventHandler<EmailVerificationRequested, EmailVerificationHandler>();
        services.AddIntegrationEventHandler<ExistingAccountRegistrationAttempted, ExistingAccountEmailHandler>();
        return services;
    }
}
