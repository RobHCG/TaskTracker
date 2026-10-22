using System.Text.Json;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using Tasks.Api.Events;
using Testcontainers.RabbitMq;

namespace Tasks.Tests;

[Category("Integration")]
public class RabbitMqEventPublisherTests
{
  private const string ExchangeName = "tasks";
  private const string RoutingKey = "task.completed";
  private const string UserName = "tasks";
  private const string Password = "tasks";

  private RabbitMqContainer _container = null!;

  [OneTimeSetUp]
  public async Task StartRabbitMq()
  {
    _container = new RabbitMqBuilder("rabbitmq:4-management")
        .WithUsername(UserName)
        .WithPassword(Password)
        .Build();

    await _container.StartAsync();
  }

  [OneTimeTearDown]
  public async Task StopRabbitMq()
  {
    await _container.DisposeAsync();
  }

  [Test]
  public async Task PublishAsync_SendsTaskCompletedToTheTasksExchange()
  {
    var connectionFactory = new ConnectionFactory
    {
      HostName = _container.Hostname,
      Port = _container.GetMappedPublicPort(5672),
      UserName = UserName,
      Password = Password
    };
    using var connection = await connectionFactory.CreateConnectionAsync();
    using var channel = await connection.CreateChannelAsync();

    await channel.ExchangeDeclareAsync(exchange: ExchangeName, type: ExchangeType.Topic, durable: true);
    var queue = await channel.QueueDeclareAsync();
    await channel.QueueBindAsync(queue: queue.QueueName, exchange: ExchangeName, routingKey: RoutingKey);

    var options = Options.Create(new RabbitMqOptions
    {
      HostName = connectionFactory.HostName,
      Port = connectionFactory.Port,
      UserName = UserName,
      Password = Password
    });
    using var publisher = new RabbitMqEventPublisher(options);
    var sent = new TaskCompleted(
        Guid.NewGuid(), 42, "Write the integration test", new DateTime(2026, 1, 15, 9, 30, 0, DateTimeKind.Utc));

    await publisher.PublishAsync(sent, CancellationToken.None);

    BasicGetResult? received = null;
    var deadline = DateTime.UtcNow.AddSeconds(5);
    while (received is null && DateTime.UtcNow < deadline)
    {
      received = await channel.BasicGetAsync(queue.QueueName, autoAck: true);
      if (received is null)
      {
        await Task.Delay(100);
      }
    }

    Assert.That(received, Is.Not.Null, "No message arrived on the queue within 5 seconds");
    var payload = JsonSerializer.Deserialize<TaskCompleted>(received.Body.Span, JsonSerializerOptions.Web);

    using (Assert.EnterMultipleScope())
    {
      Assert.That(payload, Is.EqualTo(sent));
      Assert.That(received.BasicProperties.MessageId, Is.EqualTo(sent.EventId.ToString()));
      Assert.That(received.BasicProperties.ContentType, Is.EqualTo("application/json"));
      Assert.That(received.BasicProperties.Persistent, Is.True);
    }
  }
}
