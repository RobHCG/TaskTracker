using Tasks.Api.Events;

namespace Tasks.Tests;

public class FakeEventPublisher : IEventPublisher
{
  public List<TaskCompleted> Published { get; } = [];
  public bool IsDown { get; set; }

  public Task PublishAsync(TaskCompleted taskCompleted, CancellationToken cancellationToken)
  {
    if (IsDown)
    {
      throw new InvalidOperationException("The broker is down");
    }

    Published.Add(taskCompleted);
    return Task.CompletedTask;
  }
}
