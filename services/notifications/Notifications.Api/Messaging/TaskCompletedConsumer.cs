using System.Text.Json;
using Microsoft.Extensions.Options;
using Notifications.Api.Messages;
using Notifications.Api.Services;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Notifications.Api.Messaging;

public class TaskCompletedConsumer : BackgroundService
{
  public static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

  private const string ExchangeName = "tasks";
  private const string RoutingKey = "task.completed";
  private const string QueueName = "notifications.task-completed";
  private const string DeadLetterQueueName = "notifications.task-completed.dead-letter";

  private readonly IServiceScopeFactory _scopeFactory;
  private readonly RabbitMqOptions _options;
  private readonly ILogger<TaskCompletedConsumer> _logger;

  public TaskCompletedConsumer(
      IServiceScopeFactory scopeFactory, IOptions<RabbitMqOptions> options, ILogger<TaskCompletedConsumer> logger)
  {
    _scopeFactory = scopeFactory;
    _options = options.Value;
    _logger = logger;
  }

  protected override async Task ExecuteAsync(CancellationToken stoppingToken)
  {
    var connectionFactory = new ConnectionFactory
    {
      HostName = _options.HostName,
      Port = _options.Port,
      UserName = _options.UserName,
      Password = _options.Password
    };

    using var connection = await connectionFactory.CreateConnectionAsync(stoppingToken);
    using var channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);

    await DeclareTopologyAsync(channel, stoppingToken);
    await channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 1, global: false, cancellationToken: stoppingToken);

    var consumer = new AsyncEventingBasicConsumer(channel);
    consumer.ReceivedAsync += (_, delivery) => HandleAsync(channel, delivery, stoppingToken);

    await channel.BasicConsumeAsync(QueueName, autoAck: false, consumer: consumer, cancellationToken: stoppingToken);
    _logger.LogInformation("Listening for {RoutingKey} on {QueueName}", RoutingKey, QueueName);

    await Task.Delay(Timeout.Infinite, stoppingToken);
  }

  private static async Task DeclareTopologyAsync(IChannel channel, CancellationToken cancellationToken)
  {
    await channel.ExchangeDeclareAsync(
        exchange: ExchangeName, type: ExchangeType.Topic, durable: true, cancellationToken: cancellationToken);

    await channel.QueueDeclareAsync(
        queue: DeadLetterQueueName, durable: true, exclusive: false, autoDelete: false,
        cancellationToken: cancellationToken);

    await channel.QueueDeclareAsync(
        queue: QueueName, durable: true, exclusive: false, autoDelete: false,
        arguments: new Dictionary<string, object?>
        {
          { "x-dead-letter-exchange", "" },
          { "x-dead-letter-routing-key", DeadLetterQueueName }
        },
        cancellationToken: cancellationToken);

    await channel.QueueBindAsync(
        queue: QueueName, exchange: ExchangeName, routingKey: RoutingKey, cancellationToken: cancellationToken);
  }

  private async Task HandleAsync(IChannel channel, BasicDeliverEventArgs delivery, CancellationToken stoppingToken)
  {
    try
    {
      var message = JsonSerializer.Deserialize<TaskCompleted>(delivery.Body.Span, SerializerOptions)
          ?? throw new JsonException("Message body was null");

      using var scope = _scopeFactory.CreateScope();
      var recorder = scope.ServiceProvider.GetRequiredService<NotificationRecorder>();

      if (await recorder.RecordAsync(message, stoppingToken))
      {
        _logger.LogInformation("Recorded notification for event {EventId}", message.EventId);
      }
      else
      {
        _logger.LogInformation("Ignored duplicate event {EventId}", message.EventId);
      }

      await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, cancellationToken: stoppingToken);
    }
    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
    {
      // Shutting down: leave the message unacknowledged so RabbitMQ redelivers it.
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Could not process delivery {DeliveryTag}; sending it to {DeadLetterQueue}",
          delivery.DeliveryTag, DeadLetterQueueName);
      await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: false, cancellationToken: stoppingToken);
    }
  }
}
