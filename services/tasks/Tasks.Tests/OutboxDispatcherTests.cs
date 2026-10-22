using Microsoft.AspNetCore.Components;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using NUnit.Framework.Internal;
using Tasks.Api.Data;
using Tasks.Api.Events;
using Tasks.Api.Models;
using Tasks.Api.Outbox;

namespace Tasks.Tests;

public class OutboxDispatcherTests
{
  private static readonly DateTimeOffset FixedNow = new(2026, 1, 15, 9, 30, 0, TimeSpan.Zero);

  private SqliteConnection _connection = null!;
  private TasksDbContext _db = null!;
  private FakeEventPublisher _publisher = null!;
  private OutboxDispatcher _dispatcher = null!;

  [SetUp]
  public void SetUp()
  {
    _connection = new SqliteConnection("Data Source=:memory:");
    _connection.Open();

    var options = new DbContextOptionsBuilder<TasksDbContext>()
        .UseSqlite(_connection)
        .Options;

    _db = new TasksDbContext(options);
    _db.Database.EnsureCreated();

    _publisher = new FakeEventPublisher();
    _dispatcher = new OutboxDispatcher(_db, _publisher, new FakeTimeProvider(FixedNow));
  }

  [TearDown]
  public void TearDown()
  {
    _db.Dispose();
    _connection.Dispose();
  }

  [Test]
  public async Task DispatchPendingAsync_PublishesUnsentMessage_AndMarksItSent()
  {
    var message = await SeedOutboxMessageAsync("Write the README");

    await _dispatcher.DispatchPendingAsync(CancellationToken.None);

    _db.ChangeTracker.Clear();
    var saved = await _db.OutboxMessages.SingleAsync();

    using (Assert.EnterMultipleScope())
    {
      Assert.That(_publisher.Published, Is.EqualTo(new[]
      {
        new TaskCompleted(message.EventId, message.TaskId, message.Title, message.CompletedAt)
      }));
      Assert.That(saved.SentAt, Is.EqualTo(FixedNow.UtcDateTime));
      Assert.That(saved.SentAt?.Kind, Is.EqualTo(DateTimeKind.Utc));
    }
  }

  [Test]
  public async Task DispatchPendingAsync_LeavesMessageUnsent_WhenPublishFails_AndSendsItNextRound()
  {
    var message = await SeedOutboxMessageAsync("Survive an outage");
    _publisher.IsDown = true;

    Assert.ThrowsAsync<InvalidOperationException>(() => _dispatcher.DispatchPendingAsync(CancellationToken.None));

    _db.ChangeTracker.Clear();
    Assert.That((await _db.OutboxMessages.SingleAsync()).SentAt, Is.Null);

    _publisher.IsDown = false;
    await _dispatcher.DispatchPendingAsync(CancellationToken.None);

    _db.ChangeTracker.Clear();
    var saved = await _db.OutboxMessages.SingleAsync();

    using (Assert.EnterMultipleScope())
    {
      Assert.That(_publisher.Published.Select(e => e.EventId), Is.EqualTo(new[] { message.EventId }));
      Assert.That(saved.SentAt, Is.EqualTo(FixedNow.UtcDateTime));
    }
  }

  [Test]
  public async Task DispatchPendingAsync_DoesNotResendMessage_OnTheNextRound()
  {
    await SeedOutboxMessageAsync("Send me once");

    await _dispatcher.DispatchPendingAsync(CancellationToken.None);
    await _dispatcher.DispatchPendingAsync(CancellationToken.None);

    Assert.That(_publisher.Published, Has.Count.EqualTo(1));
  }

  private async Task<OutboxMessage> SeedOutboxMessageAsync(string title)
  {
    var message = new OutboxMessage
    {
      EventId = Guid.NewGuid(),
      TaskId = 1,
      Title = title,
      CompletedAt = FixedNow.UtcDateTime.AddMinutes(-1)
    };
    _db.OutboxMessages.Add(message);
    await _db.SaveChangesAsync();
    _db.ChangeTracker.Clear();
    return message;
  }
}
