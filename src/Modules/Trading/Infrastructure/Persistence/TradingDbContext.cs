using Astryx.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Astryx.Modules.Trading.Infrastructure.Persistence;

/// <summary>Trading module data in schema <c>trading</c> (SYSTEM_DESIGN.md §10.4). No tables yet.</summary>
public sealed class TradingDbContext(DbContextOptions<TradingDbContext> options) : ModuleDbContext(options)
{
    public const string SchemaName = "trading";

    public override string Schema => SchemaName;
}
