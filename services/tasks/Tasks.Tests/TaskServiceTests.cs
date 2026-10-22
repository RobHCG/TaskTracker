using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using NUnit.Framework.Internal;
using Tasks.Api.Contracts;
using Tasks.Api.Data;
using Tasks.Api.Models;
using Tasks.Api.Services;

namespace Tasks.Tests;

public class TaskServiceTests
{
  private static readonly DateTimeOffset FixedNow = new(2026, 1, 15, 9, 30, 0, TimeSpan.Zero);

  private SqliteConnection _connection = null!;
  private TasksDbContext _db = null!;
  private TaskService _service = null!;

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

    _service = new TaskService(_db, new FakeTimeProvider(FixedNow));
  }

  [TearDown]
  public void TearDown()
  {
    _db.Dispose();
    _connection.Dispose();
  }

  [Test]
  public async Task CreateAsync_SavesTheTask()
  {
    var created = await _service.CreateAsync(
        new CreateTaskRequest("Write tests", "For the service"), CancellationToken.None);

    _db.ChangeTracker.Clear();
    var saved = await _db.Tasks.SingleAsync();

    using (Assert.EnterMultipleScope())
    {
      Assert.That(saved.Id, Is.EqualTo(created.Id));
      Assert.That(saved.Title, Is.EqualTo("Write tests"));
      Assert.That(saved.Description, Is.EqualTo("For the service"));
      Assert.That(saved.IsComplete, Is.False);
    }
  }

  [Test]
  public async Task CreateAsync_SavesCreatedAtFromTheClock()
  {
    await _service.CreateAsync(
        new CreateTaskRequest("Write tests", null), CancellationToken.None);

    _db.ChangeTracker.Clear();
    var saved = await _db.Tasks.SingleAsync();

    Assert.That(saved.CreatedAt, Is.EqualTo(FixedNow.UtcDateTime));
  }

  [Test]
  public async Task GetAllAsync_ReturnsAllTasks()
  {
    await SeedTaskAsync("First");
    await SeedTaskAsync("Second");

    var tasks = await _service.GetAllAsync(CancellationToken.None);

    Assert.That(tasks.Select(t => t.Title), Is.EqualTo(new[] { "First", "Second" }));
  }

  [Test]
  public async Task GetByIdAsync_ReturnsCreatedAtAsUtc()
  {
    var existing = await SeedTaskAsync("Check the time zone");

    var result = await _service.GetByIdAsync(existing.Id, CancellationToken.None);

    Assert.That(result, Is.Not.Null);
    Assert.That(result.CreatedAt.Kind, Is.EqualTo(DateTimeKind.Utc));
  }


  [Test]
  public async Task GetByIdAsync_ReturnsNull_WhenTaskDoesNotExist()
  {
    var result = await _service.GetByIdAsync(999, CancellationToken.None);

    Assert.That(result, Is.Null);
  }

  [Test]
  public async Task UpdateAsync_ChangesTheTask()
  {
    var existing = await SeedTaskAsync("Old title");

    await _service.UpdateAsync(
        existing.Id, new UpdateTaskRequest("New title", "Now with a description", true), CancellationToken.None);

    _db.ChangeTracker.Clear();
    var saved = await _db.Tasks.SingleAsync();

    using (Assert.EnterMultipleScope())
    {
      Assert.That(saved.Title, Is.EqualTo("New title"));
      Assert.That(saved.Description, Is.EqualTo("Now with a description"));
      Assert.That(saved.IsComplete, Is.True);
    }
  }

  [Test]
  public async Task UpdateAsync_ReturnsNull_WhenTaskDoesNotExist()
  {
    var result = await _service.UpdateAsync(
        999, new UpdateTaskRequest("Title", null, false), CancellationToken.None);

    Assert.That(result, Is.Null);
  }

  [Test]
  public async Task UpdateAsync_AddsOutboxMessage_WhenTaskBecomesComplete()
  {
    var existing = await SeedTaskAsync("Write the README");

    await _service.UpdateAsync(
        existing.Id, new UpdateTaskRequest("Write the README", null, true), CancellationToken.None);

    var messages = await SavedOutboxMessagesAsync();
    Assert.That(messages, Has.Count.EqualTo(1));
    var message = messages[0];

    using (Assert.EnterMultipleScope())
    {
      Assert.That(message.TaskId, Is.EqualTo(existing.Id));
      Assert.That(message.Title, Is.EqualTo("Write the README"));
      Assert.That(message.CompletedAt, Is.EqualTo(FixedNow.UtcDateTime));
      Assert.That(message.CompletedAt.Kind, Is.EqualTo(DateTimeKind.Utc));
      Assert.That(message.EventId, Is.Not.EqualTo(Guid.Empty));
      Assert.That(message.SentAt, Is.Null);
    }
  }

  [Test]
  public async Task UpdateAsync_AddsNoOutboxMessage_WhenTaskWasAlreadyComplete()
  {
    var existing = await SeedTaskAsync("Already done", isComplete: true);

    await _service.UpdateAsync(
        existing.Id, new UpdateTaskRequest("Already done, renamed", null, true), CancellationToken.None);

    Assert.That(await SavedOutboxMessagesAsync(), Is.Empty);
  }

  [Test]
  public async Task UpdateAsync_AddsNoOutboxMessage_WhenTaskIsReopened()
  {
    var existing = await SeedTaskAsync("Reopen me", isComplete: true);

    await _service.UpdateAsync(
        existing.Id, new UpdateTaskRequest("Reopen me", null, false), CancellationToken.None);

    Assert.That(await SavedOutboxMessagesAsync(), Is.Empty);
  }

  [Test]
  public async Task UpdateAsync_AddsNewOutboxMessage_WhenReopenedTaskIsCompletedAgain()
  {
    var existing = await SeedTaskAsync("Do it twice");

    await _service.UpdateAsync(existing.Id, new UpdateTaskRequest("Do it twice", null, true), CancellationToken.None);
    await _service.UpdateAsync(existing.Id, new UpdateTaskRequest("Do it twice", null, false), CancellationToken.None);
    await _service.UpdateAsync(existing.Id, new UpdateTaskRequest("Do it twice", null, true), CancellationToken.None);

    var messages = await SavedOutboxMessagesAsync();
    Assert.That(messages, Has.Count.EqualTo(2));
    Assert.That(messages[0].EventId, Is.Not.EqualTo(messages[1].EventId));
  }

  [Test]
  public async Task DeleteAsync_RemovesTheTask()
  {
    var existing = await SeedTaskAsync("Delete me");

    var deleted = await _service.DeleteAsync(existing.Id, CancellationToken.None);

    _db.ChangeTracker.Clear();
    var anyLeft = await _db.Tasks.AnyAsync();

    using (Assert.EnterMultipleScope())
    {
      Assert.That(deleted, Is.True);
      Assert.That(anyLeft, Is.False);
    }
  }

  [Test]
  public async Task DeleteAsync_ReturnsFalse_WhenTaskDoesNotExist()
  {
    var deleted = await _service.DeleteAsync(999, CancellationToken.None);

    Assert.That(deleted, Is.False);
  }

  private async Task<TaskItem> SeedTaskAsync(string title, bool isComplete = false)
  {
    var taskItem = new TaskItem { Title = title, IsComplete = isComplete, CreatedAt = DateTime.UtcNow };
    _db.Tasks.Add(taskItem);
    await _db.SaveChangesAsync();
    _db.ChangeTracker.Clear();
    return taskItem;
  }

  private async Task<List<OutboxMessage>> SavedOutboxMessagesAsync()
  {
    _db.ChangeTracker.Clear();
    return await _db.OutboxMessages.OrderBy(m => m.Id).ToListAsync();
  }
}
