using Astryx.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Astryx.Modules.Identity.Infrastructure.Persistence;

/// <summary>Identity module data in schema <c>identity</c> (SYSTEM_DESIGN.md §10.4). No tables yet.</summary>
public sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options) : ModuleDbContext(options)
{
    public const string SchemaName = "identity";

    public override string Schema => SchemaName;
}
