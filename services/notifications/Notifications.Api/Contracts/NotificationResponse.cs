namespace Notifications.Api.Contracts;

public record NotificationResponse(Guid EventId, int TaskId, string TaskTitle, DateTime CompletedAt, DateTime NotifiedAt);
