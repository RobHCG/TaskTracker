namespace Tasks.Api.Events;

public interface IEventPublisher
{
  Task PublishAsync(TaskCompleted taskCompleted, CancellationToken cancellationToken);
}
