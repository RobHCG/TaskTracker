namespace Tasks.Api.Models;

public class OutboxMessage
{
  public int Id { get; set; }
  public Guid EventId { get; set; }
  public int TaskId { get; set; }
  public required string Title { get; set; }
  public DateTime CompletedAt { get; set; }
  public DateTime? SentAt { get; set; }
}
