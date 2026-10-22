using Microsoft.AspNetCore.Mvc;
using Tasks.Api.Contracts;
using Tasks.Api.Models;
using Tasks.Api.Services;

namespace Tasks.Api.Controllers;

[ApiController]
[Route("api/tasks")]
public class TasksController : ControllerBase
{
  private readonly ITaskService _taskService;

  public TasksController(ITaskService taskService)
  {
    _taskService = taskService;
  }

  [HttpGet]
  public async Task<ActionResult<IReadOnlyList<TaskItem>>> GetAll(CancellationToken cancellationToken)
  {
    var tasks = await _taskService.GetAllAsync(cancellationToken);
    return Ok(tasks);
  }

  [HttpGet("{id:int}")]
  public async Task<ActionResult<TaskItem>> GetById(int id, CancellationToken cancellationToken)
  {
    var taskItem = await _taskService.GetByIdAsync(id, cancellationToken);
    if (taskItem is null)
    {
      return NotFound();
    }

    return Ok(taskItem);
  }

  [HttpPost]
  public async Task<ActionResult<TaskItem>> Create(CreateTaskRequest request, CancellationToken cancellationToken)
  {
    var taskItem = await _taskService.CreateAsync(request, cancellationToken);
    return CreatedAtAction(nameof(GetById), new { id = taskItem.Id }, taskItem);
  }

  [HttpPut("{id:int}")]
  public async Task<ActionResult<TaskItem>> Update(int id, UpdateTaskRequest request, CancellationToken cancellationToken)
  {
    var taskItem = await _taskService.UpdateAsync(id, request, cancellationToken);
    if (taskItem is null)
    {
      return NotFound();
    }

    return Ok(taskItem);
  }

  [HttpDelete("{id:int}")]
  public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
  {
    var deleted = await _taskService.DeleteAsync(id, cancellationToken);
    if (!deleted)
    {
      return NotFound();
    }

    return NoContent();
  }
}
