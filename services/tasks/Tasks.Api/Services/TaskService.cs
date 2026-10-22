using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;
using Tasks.Api.Contracts;
using Tasks.Api.Data;
using Tasks.Api.Models;

namespace Tasks.Api.Services;

public class TaskService : ITaskService
{
  private readonly TasksDbContext _db;
  private readonly TimeProvider _timeProvider;

  public TaskService(TasksDbContext db, TimeProvider timeProvider)
  {
    _db = db;
    _timeProvider = timeProvider;
  }

  public async Task<IReadOnlyList<TaskItem>> GetAllAsync(CancellationToken cancellationToken)
  {
    return await _db.Tasks
        .OrderBy(t => t.Id)
        .ToListAsync(cancellationToken);
  }

  public async Task<TaskItem?> GetByIdAsync(int id, CancellationToken cancellationToken)
  {
    return await _db.Tasks.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
  }

  public async Task<TaskItem> CreateAsync(CreateTaskRequest request, CancellationToken cancellationToken)
  {
    var taskItem = new TaskItem
    {
      Title = request.Title,
      Description = request.Description,
      CreatedAt = _timeProvider.GetUtcNow().UtcDateTime
    };

    _db.Tasks.Add(taskItem);
    await _db.SaveChangesAsync(cancellationToken);
    return taskItem;
  }

  public async Task<TaskItem?> UpdateAsync(int id, UpdateTaskRequest request, CancellationToken cancellationToken)
  {
    var taskItem = await GetByIdAsync(id, cancellationToken);
    if (taskItem is null)
    {
      return null;
    }

    var wasComplete = taskItem.IsComplete;

    taskItem.Title = request.Title;
    taskItem.Description = request.Description;
    taskItem.IsComplete = request.IsComplete;

    if (!wasComplete && taskItem.IsComplete)
    {
      _db.OutboxMessages.Add(new OutboxMessage
      {
        EventId = Guid.NewGuid(),
        TaskId = taskItem.Id,
        Title = taskItem.Title,
        CompletedAt = _timeProvider.GetUtcNow().UtcDateTime
      });
    }

    await _db.SaveChangesAsync(cancellationToken);

    return taskItem;
  }

  public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken)
  {
    var taskItem = await GetByIdAsync(id, cancellationToken);
    if (taskItem is null)
    {
      return false;
    }

    _db.Tasks.Remove(taskItem);
    await _db.SaveChangesAsync(cancellationToken);
    return true;
  }
}
