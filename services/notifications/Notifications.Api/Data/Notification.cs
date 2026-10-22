namespace Notifications.Api.Data;

public class Notification
{
  public int Id { get; set; }
  public Guid EventId { get; set; }
  public int TaskId { get; set; }
  public required string TaskTitle { get; set; }
  public DateTime CompletedAt { get; set; }
  public DateTime NotifiedAt { get; set; }
}
