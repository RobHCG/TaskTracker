using System.Text.Json;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace Tasks.Api.Events;

public class RabbitMqEventPublisher : IEventPublisher, IDisposable
{
  public static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

  private const string ExchangeName = "tasks";
  private const string TaskCompletedRoutingKey = "task.completed";

  private readonly ConnectionFactory _connectionFactory;
  private readonly SemaphoreSlim _connectionLock = new(1, 1);
  private IConnection? _connection;

  public RabbitMqEventPublisher(IOptions<RabbitMqOptions> options)
  {
    _connectionFactory = new ConnectionFactory
    {
      HostName = options.Value.HostName,
      Port = options.Value.Port,
      UserName = options.Value.UserName,
      Password = options.Value.Password
    };
  }

  public async Task PublishAsync(TaskCompleted taskCompleted, CancellationToken cancellationToken)
  {
    var connection = await GetConnectionAsync(cancellationToken);
    using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);

    await channel.ExchangeDeclareAsync(
        exchange: ExchangeName, type: ExchangeType.Topic, durable: true, cancellationToken: cancellationToken);

    var properties = new BasicProperties
    {
      Persistent = true,
      ContentType = "application/json",
      MessageId = taskCompleted.EventId.ToString()
    };
    var body = JsonSerializer.SerializeToUtf8Bytes(taskCompleted, SerializerOptions);

    await channel.BasicPublishAsync(
        exchange: ExchangeName,
        routingKey: TaskCompletedRoutingKey,
        mandatory: false,
        basicProperties: properties,
        body: body,
        cancellationToken: cancellationToken);
  }

  private async Task<IConnection> GetConnectionAsync(CancellationToken cancellationToken)
  {
    if (_connection is not null)
    {
      return _connection;
    }

    await _connectionLock.WaitAsync(cancellationToken);
    try
    {
      _connection ??= await _connectionFactory.CreateConnectionAsync(cancellationToken);
      return _connection;
    }
    finally
    {
      _connectionLock.Release();
    }
  }

  public void Dispose()
  {
    _connection?.Dispose();
    _connectionLock.Dispose();
  }
}
