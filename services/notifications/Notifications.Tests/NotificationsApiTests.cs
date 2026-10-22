using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Notifications.Api.Contracts;
using Notifications.Api.Data;
using Notifications.Api.Messaging;

namespace Notifications.Tests;

public class NotificationsApiTests
{
  private SqliteConnection _keepAlive = null!;
  private WebApplicationFactory<Program> _factory = null!;
  private HttpClient _client = null!;

  [SetUp]
  public void SetUp()
  {
    var connectionString = $"Data Source=notifications-{Guid.NewGuid()};Mode=Memory;Cache=Shared";
    _keepAlive = new SqliteConnection(connectionString);
    _keepAlive.Open();

    _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
    {
      builder.UseSetting("ConnectionStrings:NotificationsDb", connectionString);
      builder.UseSetting("Database:MigrateOnStartup", "true");
      builder.ConfigureTestServices(services =>
      {
        var consumer = services.Single(d => d.ImplementationType == typeof(TaskCompletedConsumer));
        services.Remove(consumer);
      });
    });

    _client = _factory.CreateClient();
  }

  [TearDown]
  public void TearDown()
  {
    _client.Dispose();
    _factory.Dispose();
    _keepAlive.Dispose();
  }

  [Test]
  public async Task GetAll_ReturnsEmptyList_WhenNothingHasBeenRecorded()
  {
    var response = await _client.GetAsync("/api/notifications");
    var body = await response.Content.ReadAsStringAsync();

    using (Assert.EnterMultipleScope())
    {
      Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
      Assert.That(body, Is.EqualTo("[]"));
    }
  }

  [Test]
  public async Task GetAll_ReturnsNotifications_NewestFirst()
  {
    using (var scope = _factory.Services.CreateScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
      db.Notifications.Add(NewNotification("Older", new DateTime(2026, 1, 15, 9, 0, 0, DateTimeKind.Utc)));
      db.Notifications.Add(NewNotification("Newer", new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc)));
      await db.SaveChangesAsync();
    }

    var notifications = await _client.GetFromJsonAsync<List<NotificationResponse>>("/api/notifications");

    Assert.That(notifications!.Select(n => n.TaskTitle), Is.EqualTo(new[] { "Newer", "Older" }));
  }

  private static Notification NewNotification(string title, DateTime notifiedAt) => new()
  {
    EventId = Guid.NewGuid(),
    TaskId = 1,
    TaskTitle = title,
    CompletedAt = notifiedAt.AddMinutes(-1),
    NotifiedAt = notifiedAt
  };
}
