using Astryx.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Astryx.Modules.Discovery.Infrastructure.Persistence;

/// <summary>Discovery module data in schema <c>discovery</c> (SYSTEM_DESIGN.md §10.4). No tables yet.</summary>
public sealed class DiscoveryDbContext(DbContextOptions<DiscoveryDbContext> options) : ModuleDbContext(options)
{
    public const string SchemaName = "discovery";

    public override string Schema => SchemaName;
}
