using Astryx.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Astryx.Modules.Portfolio.Infrastructure.Persistence;

/// <summary>Portfolio module data in schema <c>portfolio</c> (SYSTEM_DESIGN.md §10.4). No tables yet.</summary>
public sealed class PortfolioDbContext(DbContextOptions<PortfolioDbContext> options) : ModuleDbContext(options)
{
    public const string SchemaName = "portfolio";

    public override string Schema => SchemaName;
}
