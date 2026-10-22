using Microsoft.EntityFrameworkCore;
using Tasks.Api.Models;

namespace Tasks.Api.Data;

public class TasksDbContext : DbContext
{
  public TasksDbContext(DbContextOptions<TasksDbContext> options) : base(options)
  {
  }

  public DbSet<TaskItem> Tasks => Set<TaskItem>();
  public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

  protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
  {
    configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
  }
}
