using Astryx.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Astryx.Modules.Paper.Infrastructure.Persistence;

/// <summary>Paper module data in schema <c>trading</c> (SYSTEM_DESIGN.md §10.4). No tables yet.</summary>
public sealed class PaperDbContext(DbContextOptions<PaperDbContext> options) : ModuleDbContext(options)
{
    public const string SchemaName = "trading";

    public override string Schema => SchemaName;
}
