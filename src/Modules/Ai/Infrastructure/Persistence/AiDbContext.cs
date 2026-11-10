using Astryx.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Astryx.Modules.Ai.Infrastructure.Persistence;

/// <summary>Ai module data in schema <c>ai</c> (SYSTEM_DESIGN.md §10.4). No tables yet.</summary>
public sealed class AiDbContext(DbContextOptions<AiDbContext> options) : ModuleDbContext(options)
{
    public const string SchemaName = "ai";

    public override string Schema => SchemaName;
}
