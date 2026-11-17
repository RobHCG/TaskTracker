using Refit;

namespace EndToEnd.Tests;

public interface INotificationsApi
{
    [Get("/api/notifications")]
    Task<List<NotificationDto>> GetNotifications();
}

public record NotificationDto(int TaskId, string TaskTitle);
