using Microsoft.EntityFrameworkCore;
using PerfumeShop.Modules.Notifications.Domain;
using PerfumeShop.Shared.Infrastructure.Outbox;
using PerfumeShop.Shared.Infrastructure.Persistence;

namespace PerfumeShop.Modules.Notifications.Infrastructure;

internal sealed class NotificationsDbContext(DbContextOptions<NotificationsDbContext> options) : ModuleDbContext(options)
{
    internal const string SchemaName = "notifications";

    public DbSet<NotificationTemplate> Templates => Set<NotificationTemplate>();

    public DbSet<NotificationMessage> Messages => Set<NotificationMessage>();

    public DbSet<NotificationAttempt> Attempts => Set<NotificationAttempt>();

    public DbSet<StaffAlertSettings> StaffAlertSettings => Set<StaffAlertSettings>();

    public DbSet<InboxMessage> Inbox => Set<InboxMessage>();

    protected override string Schema => SchemaName;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyInbox();
    }
}
