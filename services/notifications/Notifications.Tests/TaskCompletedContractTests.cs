using Notifications.Api.Messages;
using Notifications.Api.Messaging;
using PactNet;
using PactNet.Matchers;

namespace Notifications.Tests;

public class TaskCompletedContractTests
{
  private static readonly string PactDir = Path.GetFullPath(
      Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "..", "..", "..", "..", "pacts"));

  private const string GuidPattern = "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$";
  private const string UtcTimestampPattern = @"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d+)?Z$";
  private const string ExampleTitle = "Caught by the queue";

  [Test]
  public void Notifications_CanHandleATaskCompletedEvent()
  {
    var pact = Pact.V4("Notifications", "Tasks", new PactConfig
    {
      PactDir = PactDir,
      DefaultJsonSettings = TaskCompletedConsumer.SerializerOptions
    }).WithMessageInteractions();

    pact.ExpectsToReceive("a task completed event")
        .WithJsonContent(new
        {
          eventId = Match.Regex("8bd95168-5139-4a6e-8574-6c3d064d9d64", GuidPattern),
          taskId = Match.Integer(3),
          title = Match.Type(ExampleTitle),
          completedAt = Match.Regex("2026-10-13T13:02:37.5199989Z", UtcTimestampPattern)
        })
        .Verify<TaskCompleted>(message =>
        {
          Assert.That(message.Title, Is.EqualTo(ExampleTitle));
          Assert.That(message.CompletedAt.Kind, Is.EqualTo(DateTimeKind.Utc));
        });
  }
}
