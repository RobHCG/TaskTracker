using System.Net;
using System.Net.Http.Json;
using System.Diagnostics;

namespace EndToEnd.Tests;

[Category("EndToEnd")]
public class TaskCompletionFlowTests
{
    private static readonly TimeSpan NotificationTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

    private HttpClient _tasksApi = null!;
    private HttpClient _notificationsApi = null!;

    [OneTimeSetUp]
    public void CreateClients()
    {
        _tasksApi = new HttpClient { BaseAddress = BaseUrl("TASKS_API_URL", "http://localhost:5080") };
        _notificationsApi = new HttpClient { BaseAddress = BaseUrl("NOTIFICATIONS_API_URL", "http://localhost:5090") };
    }

    [OneTimeTearDown]
    public void DisposeClients()
    {
        _tasksApi.Dispose();
        _notificationsApi.Dispose();
    }

    private static Uri BaseUrl(string environmentVariable, string fallback) =>
        new(Environment.GetEnvironmentVariable(environmentVariable) ?? fallback);

    [Test]
    public async Task CompletingATask_CreatesANotification()
    {
        var title = $"End-to-end {Guid.NewGuid()}";

        var createResponse = await _tasksApi.PostAsJsonAsync("api/tasks", new { title });
        Assert.That(createResponse.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        var task = await createResponse.Content.ReadFromJsonAsync<TaskDto>();

        var completeResponse = await _tasksApi.PutAsJsonAsync($"api/tasks/{task!.Id}", new { title, isComplete = true });
        Assert.That(completeResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var notification = await WaitForNotificationAsync(title);

        Assert.That(notification, Is.Not.Null, $"No notification for '{title}' within {NotificationTimeout.TotalSeconds}s");
        Assert.That(notification.TaskId, Is.EqualTo(task.Id));
    }

    private async Task<NotificationDto?> WaitForNotificationAsync(string title)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < NotificationTimeout)
        {
            var notifications = await _notificationsApi.GetFromJsonAsync<List<NotificationDto>>("api/notifications");
            var match = notifications!.SingleOrDefault(n => n.TaskTitle == title);
            if (match is not null)
                return match;

            await Task.Delay(PollInterval);
        }

        return null;
    }

    private record TaskDto(int Id);
    private record NotificationDto(int TaskId, string TaskTitle);
}
