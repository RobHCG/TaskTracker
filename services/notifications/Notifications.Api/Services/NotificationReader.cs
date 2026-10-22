using Microsoft.EntityFrameworkCore;
using Notifications.Api.Contracts;
using Notifications.Api.Data;

namespace Notifications.Api.Services;

public class NotificationReader
{
  private readonly NotificationsDbContext _db;

  public NotificationReader(NotificationsDbContext db)
  {
    _db = db;
  }

  public async Task<IReadOnlyList<NotificationResponse>> GetAllAsync(CancellationToken cancellationToken)
  {
    return await _db.Notifications
        .OrderByDescending(n => n.NotifiedAt)
        .ThenByDescending(n => n.Id)
        .Select(n => new NotificationResponse(n.EventId, n.TaskId, n.TaskTitle, n.CompletedAt, n.NotifiedAt))
        .ToListAsync(cancellationToken);
  }
}
