using Refit;

namespace EndToEnd.Tests;

public interface ITasksApi
{
    [Post("/api/tasks")]
    Task<ApiResponse<TaskDto>> CreateTask([Body] CreateTaskRequest request);

    [Put("/api/tasks/{id}")]
    Task<ApiResponse<TaskDto>> UpdateTask(int id, [Body] UpdateTaskRequest request);
}

public record CreateTaskRequest(string Title);

public record UpdateTaskRequest(string Title, bool IsComplete);

public record TaskDto(int Id);
