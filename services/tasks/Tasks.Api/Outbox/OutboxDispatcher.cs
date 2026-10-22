using Microsoft.EntityFrameworkCore;
using Tasks.Api.Data;
using Tasks.Api.Events;

namespace Tasks.Api.Outbox;

public class OutboxDispatcher
{
  private readonly TasksDbContext _db;
  private readonly IEventPublisher _publisher;
  private readonly TimeProvider _timeProvider;

  public OutboxDispatcher(TasksDbContext db, IEventPublisher publisher, TimeProvider timeProvider)
  {
    _db = db;
    _publisher = publisher;
    _timeProvider = timeProvider;
  }

  public async Task DispatchPendingAsync(CancellationToken cancellationToken)
  {
    var pending = await _db.OutboxMessages
        .Where(m => m.SentAt == null)
        .OrderBy(m => m.Id)
        .ToListAsync(cancellationToken);

    foreach (var message in pending)
    {
      await _publisher.PublishAsync(
          new TaskCompleted(message.EventId, message.TaskId, message.Title, message.CompletedAt),
          cancellationToken);

      message.SentAt = _timeProvider.GetUtcNow().UtcDateTime;
      await _db.SaveChangesAsync(cancellationToken);
    }
  }
}
