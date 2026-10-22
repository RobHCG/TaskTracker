using Microsoft.EntityFrameworkCore;

namespace Notifications.Api.Data;

public class NotificationsDbContext : DbContext
{
  public NotificationsDbContext(DbContextOptions<NotificationsDbContext> options) : base(options)
  {
  }

  public DbSet<Notification> Notifications => Set<Notification>();

  protected override void OnModelCreating(ModelBuilder modelBuilder)
  {
    modelBuilder.Entity<Notification>()
        .HasIndex(n => n.EventId)
        .IsUnique();
  }

  protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
  {
    configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
  }
}
