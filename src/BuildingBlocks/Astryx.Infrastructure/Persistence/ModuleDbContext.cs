using Microsoft.EntityFrameworkCore;

namespace Astryx.Infrastructure.Persistence;

/// <summary>
/// Base for every module's DbContext: one Postgres schema per module, snake_case names, decimal defaults for money
/// (SYSTEM_DESIGN.md §4.4, §10.1). Derived contexts configure their tables in <see cref="ConfigureModel"/>.
/// </summary>
public abstract class ModuleDbContext(DbContextOptions options) : DbContext(options)
{
    /// <summary>The Postgres schema this context owns.</summary>
    public abstract string Schema { get; }

    protected sealed override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        ConfigureModel(modelBuilder);
    }

    protected virtual void ConfigureModel(ModelBuilder modelBuilder)
    {
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Money by default; prices (18,6), quantities (18,6) and ratios (9,6) override per property (§10.1).
        configurationBuilder.Properties<decimal>().HavePrecision(20, 4);
    }
}
