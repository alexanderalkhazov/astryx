using Astryx.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Astryx.Modules.Profiles.Infrastructure.Persistence;

/// <summary>Profiles module data in schema <c>identity</c> (SYSTEM_DESIGN.md §10.4). No tables yet.</summary>
public sealed class ProfilesDbContext(DbContextOptions<ProfilesDbContext> options) : ModuleDbContext(options)
{
    public const string SchemaName = "identity";

    public override string Schema => SchemaName;
}
