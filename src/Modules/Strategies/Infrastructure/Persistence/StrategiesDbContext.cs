using Astryx.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Astryx.Modules.Strategies.Infrastructure.Persistence;

/// <summary>Strategies module data in schema <c>strategy</c> (SYSTEM_DESIGN.md §10.4). No tables yet.</summary>
public sealed class StrategiesDbContext(DbContextOptions<StrategiesDbContext> options) : ModuleDbContext(options)
{
    public const string SchemaName = "strategy";

    public override string Schema => SchemaName;
}
