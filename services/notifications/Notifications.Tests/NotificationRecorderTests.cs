using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Notifications.Api.Data;
using Notifications.Api.Messages;
using Notifications.Api.Services;

namespace Notifications.Tests;

public class NotificationRecorderTests
{
  private static readonly DateTimeOffset FixedNow = new(2026, 1, 15, 9, 31, 0, TimeSpan.Zero);
  private static readonly DateTime CompletedAt = new(2026, 1, 15, 9, 30, 0, DateTimeKind.Utc);

  private SqliteConnection _connection = null!;
  private NotificationsDbContext _db = null!;
  private NotificationRecorder _recorder = null!;

  [SetUp]
  public void SetUp()
  {
    _connection = new SqliteConnection("Data Source=:memory:");
    _connection.Open();

    var options = new DbContextOptionsBuilder<NotificationsDbContext>()
        .UseSqlite(_connection)
        .Options;

    _db = new NotificationsDbContext(options);
    _db.Database.EnsureCreated();

    _recorder = new NotificationRecorder(_db, new FakeTimeProvider(FixedNow));
  }

  [TearDown]
  public void TearDown()
  {
    _db.Dispose();
    _connection.Dispose();
  }

  [Test]
  public async Task RecordAsync_SavesNotification_ForANewEvent()
  {
    var message = new TaskCompleted(Guid.NewGuid(), 7, "Write the README", CompletedAt);

    var recorded = await _recorder.RecordAsync(message, CancellationToken.None);

    _db.ChangeTracker.Clear();
    var saved = await _db.Notifications.SingleAsync();

    using (Assert.EnterMultipleScope())
    {
      Assert.That(recorded, Is.True);
      Assert.That(saved.EventId, Is.EqualTo(message.EventId));
      Assert.That(saved.TaskId, Is.EqualTo(7));
      Assert.That(saved.TaskTitle, Is.EqualTo("Write the README"));
      Assert.That(saved.CompletedAt, Is.EqualTo(CompletedAt));
      Assert.That(saved.NotifiedAt, Is.EqualTo(FixedNow.UtcDateTime));
    }
  }

  [Test]
  public async Task RecordAsync_IgnoresDuplicate_WhenTheSameEventArrivesTwice()
  {
    var message = new TaskCompleted(Guid.NewGuid(), 7, "Write the README", CompletedAt);

    await _recorder.RecordAsync(message, CancellationToken.None);
    var recordedAgain = await _recorder.RecordAsync(message, CancellationToken.None);

    _db.ChangeTracker.Clear();
    var count = await _db.Notifications.CountAsync();

    using (Assert.EnterMultipleScope())
    {
      Assert.That(recordedAgain, Is.False);
      Assert.That(count, Is.EqualTo(1));
    }
  }

  [Test]
  public async Task RecordAsync_RecordsBoth_WhenTheSameTaskIsCompletedAgainWithANewEvent()
  {
    await _recorder.RecordAsync(
        new TaskCompleted(Guid.NewGuid(), 7, "Do it twice", CompletedAt), CancellationToken.None);
    await _recorder.RecordAsync(
        new TaskCompleted(Guid.NewGuid(), 7, "Do it twice", CompletedAt.AddHours(1)), CancellationToken.None);

    _db.ChangeTracker.Clear();
    var count = await _db.Notifications.CountAsync();

    Assert.That(count, Is.EqualTo(2));
  }

  [Test]
  public void Database_RejectsTwoNotificationsWithTheSameEventId()
  {
    var eventId = Guid.NewGuid();
    _db.Notifications.Add(new Notification
    {
      EventId = eventId,
      TaskId = 1,
      TaskTitle = "First",
      CompletedAt = CompletedAt,
      NotifiedAt = CompletedAt
    });
    _db.Notifications.Add(new Notification
    {
      EventId = eventId,
      TaskId = 1,
      TaskTitle = "Second",
      CompletedAt = CompletedAt,
      NotifiedAt = CompletedAt
    });

    Assert.ThrowsAsync<DbUpdateException>(() => _db.SaveChangesAsync());
  }
}
