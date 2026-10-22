using Microsoft.AspNetCore.Mvc;
using Notifications.Api.Contracts;
using Notifications.Api.Services;

namespace Notifications.Api.Controllers;

[ApiController]
[Route("api/notifications")]
public class NotificationsController : ControllerBase
{
  private readonly NotificationReader _reader;

  public NotificationsController(NotificationReader reader)
  {
    _reader = reader;
  }

  [HttpGet]
  public async Task<ActionResult<IReadOnlyList<NotificationResponse>>> GetAll(CancellationToken cancellationToken)
  {
    return Ok(await _reader.GetAllAsync(cancellationToken));
  }
}
