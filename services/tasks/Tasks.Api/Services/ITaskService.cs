using Tasks.Api.Contracts;
using Tasks.Api.Models;

namespace Tasks.Api.Services;

public interface ITaskService
{
  Task<IReadOnlyList<TaskItem>> GetAllAsync(CancellationToken cancellationToken);
  Task<TaskItem?> GetByIdAsync(int id, CancellationToken cancellationToken);
  Task<TaskItem> CreateAsync(CreateTaskRequest request, CancellationToken cancellationToken);
  Task<TaskItem?> UpdateAsync(int id, UpdateTaskRequest request, CancellationToken cancellationToken);
  Task<bool> DeleteAsync(int id, CancellationToken cancellationToken);
}
