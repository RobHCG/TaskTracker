namespace Tasks.Api.Events;

public record TaskCompleted(Guid EventId, int TaskId, string Title, DateTime CompletedAt);
