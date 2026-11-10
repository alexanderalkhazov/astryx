using Astryx.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Astryx.Modules.Notifications.Infrastructure.Persistence;

/// <summary>Notifications module data in schema <c>notify</c> (SYSTEM_DESIGN.md §10.4). No tables yet.</summary>
public sealed class NotificationsDbContext(DbContextOptions<NotificationsDbContext> options) : ModuleDbContext(options)
{
    public const string SchemaName = "notify";

    public override string Schema => SchemaName;
}
