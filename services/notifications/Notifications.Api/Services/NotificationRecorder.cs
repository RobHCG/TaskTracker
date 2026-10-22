using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Notifications.Api.Data;
using Notifications.Api.Messages;

namespace Notifications.Api.Services;

public class NotificationRecorder
{
  private const int SqliteUniqueConstraintFailed = 2067;

  private readonly NotificationsDbContext _db;
  private readonly TimeProvider _timeProvider;

  public NotificationRecorder(NotificationsDbContext db, TimeProvider timeProvider)
  {
    
    _db = db;
    _timeProvider = timeProvider;
  }

  public async Task<bool> RecordAsync(TaskCompleted message, CancellationToken cancellationToken)
  {
    if (await _db.Notifications.AnyAsync(n => n.EventId == message.EventId, cancellationToken))
    {
      return false;
    }

    _db.Notifications.Add(new Notification
    {
      EventId = message.EventId,
      TaskId = message.TaskId,
      TaskTitle = message.Title,
      CompletedAt = message.CompletedAt,
      NotifiedAt = _timeProvider.GetUtcNow().UtcDateTime
    });

    try
    {
      await _db.SaveChangesAsync(cancellationToken);
      return true;
    }
    catch (DbUpdateException ex) when (ex.InnerException is SqliteException { SqliteExtendedErrorCode: SqliteUniqueConstraintFailed })
    {
      _db.ChangeTracker.Clear();
      return false;
    }
  }
}
