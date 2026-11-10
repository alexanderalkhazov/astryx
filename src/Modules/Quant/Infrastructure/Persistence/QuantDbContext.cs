using Astryx.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Astryx.Modules.Quant.Infrastructure.Persistence;

/// <summary>Quant module data in schema <c>market</c> (SYSTEM_DESIGN.md §10.4). No tables yet.</summary>
public sealed class QuantDbContext(DbContextOptions<QuantDbContext> options) : ModuleDbContext(options)
{
    public const string SchemaName = "market";

    public override string Schema => SchemaName;
}
