using PactNet.Verifier;
using Tasks.Api.Events;

namespace Tasks.Tests;

public class TaskCompletedContractTests
{
  private static readonly FileInfo PactFile = new(Path.GetFullPath(Path.Combine(
      TestContext.CurrentContext.TestDirectory, "..", "..", "..", "..", "..", "..", "pacts", "Notifications-Tasks.json")));

  [Test]
  public void Tasks_HonoursTheNotificationsContract()
  {
    Assert.That(PactFile.Exists, Is.True,
        $"Contract file not found at {PactFile.FullName}. Run the Notifications tests first.");

    using var verifier = new PactVerifier("Tasks");

    // Workaround for https://github.com/pact-foundation/pact-net/issues/558: message-only
    // verification calls message:// unless an HTTP endpoint is registered first.
        verifier
        .WithHttpEndpoint(new Uri("http://localhost:49152"))
        .WithMessages(scenarios =>
        {
          scenarios.Add("a task completed event", () => new TaskCompleted(
              Guid.NewGuid(), 42, "Write the contract test", new DateTime(2026, 1, 15, 9, 30, 0, DateTimeKind.Utc)));
        }, RabbitMqEventPublisher.SerializerOptions)
        .WithFileSource(PactFile)
        .Verify();
  }
}
