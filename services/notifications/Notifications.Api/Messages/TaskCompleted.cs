namespace Notifications.Api.Messages;

public record TaskCompleted(Guid EventId, int TaskId, string Title, DateTime CompletedAt);
