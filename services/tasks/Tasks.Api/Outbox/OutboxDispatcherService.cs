namespace Tasks.Api.Outbox;

public class OutboxDispatcherService : BackgroundService
{
  private static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);

  private readonly IServiceScopeFactory _scopeFactory;
  private readonly ILogger<OutboxDispatcherService> _logger;

  public OutboxDispatcherService(IServiceScopeFactory scopeFactory, ILogger<OutboxDispatcherService> logger)
  {
    _scopeFactory = scopeFactory;
    _logger = logger;
  }

  protected override async Task ExecuteAsync(CancellationToken stoppingToken)
  {
    using var timer = new PeriodicTimer(Interval);

    while (await timer.WaitForNextTickAsync(stoppingToken))
    {
      try
      {
        using var scope = _scopeFactory.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<OutboxDispatcher>();
        await dispatcher.DispatchPendingAsync(stoppingToken);
      }
      catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
      {
        _logger.LogWarning(ex, "Outbox dispatch failed; retrying in {Interval}", Interval);
      }
    }
  }
}
