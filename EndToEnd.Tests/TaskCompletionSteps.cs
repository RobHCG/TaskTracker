using System.Diagnostics;
using System.Net;
using Reqnroll;

namespace EndToEnd.Tests;

[Binding]
public class TaskCompletionSteps
{
    private static readonly TimeSpan NotificationTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

    private readonly ITasksApi _tasksApi;
    private readonly INotificationsApi _notificationsApi;

    private string _title = null!;
    private int _taskId;

    public TaskCompletionSteps(ITasksApi tasksApi, INotificationsApi notificationsApi)
    {
        _tasksApi = tasksApi;
        _notificationsApi = notificationsApi;
    }

    [Given("I have an open task")]
    public async Task GivenIHaveAnOpenTask()
    {
        _title = $"End-to-end {Guid.NewGuid()}";

        var response = await _tasksApi.CreateTask(new CreateTaskRequest(_title));
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created), () => response.Error?.Message ?? string.Empty);
        _taskId = response.Content!.Id;
    }

    [When("I complete the task")]
    public Task WhenICompleteTheTask() => SetCompletion(true);

    [When("I reopen the task")]
    public Task WhenIReopenTheTask() => SetCompletion(false);

    [Then("a notification for the task appears")]
    public Task ThenANotificationForTheTaskAppears() => ThenNotificationsForTheTaskAppear(1);

    [Then("{int} notifications for the task appear")]
    public async Task ThenNotificationsForTheTaskAppear(int expectedCount)
    {
        var notifications = await WaitForNotificationsAsync(expectedCount);

        Assert.That(notifications, Has.Count.EqualTo(expectedCount),
            $"Expected {expectedCount} notification(s) for '{_title}' within {NotificationTimeout.TotalSeconds}s");
        Assert.That(notifications, Has.All.Matches<NotificationDto>(n => n.TaskId == _taskId));
    }

    private async Task SetCompletion(bool isComplete)
    {
        var response = await _tasksApi.UpdateTask(_taskId, new UpdateTaskRequest(_title, isComplete));
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), () => response.Error?.Message ?? string.Empty);
    }

    private async Task<List<NotificationDto>> WaitForNotificationsAsync(int expectedCount)
    {
        var stopwatch = Stopwatch.StartNew();
        List<NotificationDto> matches;
        do
        {
            var notifications = await _notificationsApi.GetNotifications();
            matches = notifications.Where(n => n.TaskTitle == _title).ToList();
            if (matches.Count >= expectedCount)
                break;

            await Task.Delay(PollInterval);
        } while (stopwatch.Elapsed < NotificationTimeout);

        return matches;
    }
}
