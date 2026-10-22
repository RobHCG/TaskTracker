using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Notifications.Api.Data;
using RabbitMQ.Client;
using Testcontainers.RabbitMq;

namespace Notifications.Tests;

[Category("Integration")]
public class TaskCompletedConsumerTests
{
  private const string ExchangeName = "tasks";
  private const string RoutingKey = "task.completed";
  private const string QueueName = "notifications.task-completed";
  private const string DeadLetterQueueName = "notifications.task-completed.dead-letter";
  private const string UserName = "tasks";
  private const string Password = "tasks";

  private RabbitMqContainer _rabbitMq = null!;
  private IConnection _connection = null!;
  private IChannel _channel = null!;
  private SqliteConnection _keepAlive = null!;
  private WebApplicationFactory<Program> _factory = null!;

  [OneTimeSetUp]
  public async Task StartRabbitMq()
  {
    _rabbitMq = new RabbitMqBuilder("rabbitmq:4-management")
        .WithUsername(UserName)
        .WithPassword(Password)
        .Build();
    await _rabbitMq.StartAsync();

    var connectionFactory = new ConnectionFactory
    {
      HostName = _rabbitMq.Hostname,
      Port = _rabbitMq.GetMappedPublicPort(5672),
      UserName = UserName,
      Password = Password
    };
    _connection = await connectionFactory.CreateConnectionAsync();
    _channel = await _connection.CreateChannelAsync();

    await _channel.ExchangeDeclareAsync(exchange: ExchangeName, type: ExchangeType.Topic, durable: true);
    await _channel.QueueDeclareAsync(
        queue: DeadLetterQueueName, durable: true, exclusive: false, autoDelete: false);
    await _channel.QueueDeclareAsync(
        queue: QueueName, durable: true, exclusive: false, autoDelete: false,
        arguments: new Dictionary<string, object?>
        {
          { "x-dead-letter-exchange", "" },
          { "x-dead-letter-routing-key", DeadLetterQueueName }
        });
    await _channel.QueueBindAsync(queue: QueueName, exchange: ExchangeName, routingKey: RoutingKey);
  }

  [OneTimeTearDown]
  public async Task StopRabbitMq()
  {
    _channel.Dispose();
    _connection.Dispose();
    await _rabbitMq.DisposeAsync();
  }

  [SetUp]
  public void StartNotificationsApi()
  {
    var connectionString = $"Data Source=notifications-{Guid.NewGuid()};Mode=Memory;Cache=Shared";
    _keepAlive = new SqliteConnection(connectionString);
    _keepAlive.Open();

    _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
    {
      builder.UseSetting("ConnectionStrings:NotificationsDb", connectionString);
      builder.UseSetting("Database:MigrateOnStartup", "true");
      builder.UseSetting("RabbitMq:HostName", _rabbitMq.Hostname);
      builder.UseSetting("RabbitMq:Port", _rabbitMq.GetMappedPublicPort(5672).ToString());
      builder.UseSetting("RabbitMq:UserName", UserName);
      builder.UseSetting("RabbitMq:Password", Password);
    });

    _ = _factory.Services;
  }

  [TearDown]
  public void StopNotificationsApi()
  {
    _factory.Dispose();
    _keepAlive.Dispose();
  }

  [Test]
  public async Task PublishedEvent_IsRecordedAsANotification()
  {
    var eventId = Guid.NewGuid();

    await PublishAsync(TaskCompletedJson(eventId, 7, "Write the README"));

    var notifications = await WaitUntilAsync(n => n.Count >= 1);

    using (Assert.EnterMultipleScope())
    {
      Assert.That(notifications, Has.Count.EqualTo(1));
      Assert.That(notifications[0].EventId, Is.EqualTo(eventId));
      Assert.That(notifications[0].TaskTitle, Is.EqualTo("Write the README"));
    }
  }

  [Test]
  public async Task SameEventPublishedTwice_IsRecordedOnce()
  {
    var duplicated = TaskCompletedJson(Guid.NewGuid(), 7, "Write the README");
    var markerEventId = Guid.NewGuid();

    await PublishAsync(duplicated);
    await PublishAsync(duplicated);
    await PublishAsync(TaskCompletedJson(markerEventId, 8, "Marker"));

    var notifications = await WaitUntilAsync(n => n.Any(x => x.EventId == markerEventId));

    Assert.That(notifications, Has.Count.EqualTo(2));
  }

  [Test]
  public async Task MessageThatCannotBeRead_GoesToTheDeadLetterQueue()
  {
    await PublishAsync(Encoding.UTF8.GetBytes("this is not json"));

    var deadLettered = await WaitForDeadLetterAsync();

    Assert.That(deadLettered, Is.Not.Null, "Nothing arrived on the dead-letter queue within 10 seconds");
    Assert.That(Encoding.UTF8.GetString(deadLettered.Body.Span), Is.EqualTo("this is not json"));
  }

  private static byte[] TaskCompletedJson(Guid eventId, int taskId, string title) =>
      JsonSerializer.SerializeToUtf8Bytes(new
      {
        eventId,
        taskId,
        title,
        completedAt = new DateTime(2026, 1, 15, 9, 30, 0, DateTimeKind.Utc)
      });

  private async Task PublishAsync(byte[] body)
  {
    await _channel.BasicPublishAsync(exchange: ExchangeName, routingKey: RoutingKey, body: body);
  }

  private async Task<List<Notification>> WaitUntilAsync(Func<List<Notification>, bool> condition)
  {
    var deadline = DateTime.UtcNow.AddSeconds(10);
    while (true)
    {
      using var scope = _factory.Services.CreateScope();
      var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
      var notifications = await db.Notifications.ToListAsync();

      if (condition(notifications) || DateTime.UtcNow > deadline)
      {
        return notifications;
      }

      await Task.Delay(100);
    }
  }

  private async Task<BasicGetResult?> WaitForDeadLetterAsync()
  {
    var deadline = DateTime.UtcNow.AddSeconds(10);
    while (DateTime.UtcNow < deadline)
    {
      var result = await _channel.BasicGetAsync(DeadLetterQueueName, autoAck: true);
      if (result is not null)
      {
        return result;
      }

      await Task.Delay(100);
    }

    return null;
  }
}
