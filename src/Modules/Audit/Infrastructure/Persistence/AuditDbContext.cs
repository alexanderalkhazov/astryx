using Astryx.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Astryx.Modules.Audit.Infrastructure.Persistence;

/// <summary>Audit module data in schema <c>audit</c> (SYSTEM_DESIGN.md §10.4). No tables yet.</summary>
public sealed class AuditDbContext(DbContextOptions<AuditDbContext> options) : ModuleDbContext(options)
{
    public const string SchemaName = "audit";

    public override string Schema => SchemaName;
}
