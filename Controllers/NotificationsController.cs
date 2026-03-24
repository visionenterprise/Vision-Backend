using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using vision_backend.Application.DTOs.Notifications;
using vision_backend.Application.Interfaces;

namespace vision_backend.Controllers;

[ApiController]
[Route("api/notifications")]
[Authorize]
public class NotificationsController : ControllerBase
{
    private readonly INotificationService _notificationService;

    public NotificationsController(INotificationService notificationService)
    {
        _notificationService = notificationService;
    }

    [HttpGet]
    public async Task<ActionResult<List<NotificationResponse>>> GetRecent([FromQuery] int take = 20)
    {
        if (!TryGetUserId(out var userId))
            return Unauthorized();

        var items = await _notificationService.GetRecentAsync(userId, take);
        return Ok(items);
    }

    [HttpGet("badge")]
    public async Task<ActionResult<NotificationBadgeResponse>> GetBadge()
    {
        if (!TryGetUserId(out var userId))
            return Unauthorized();

        var badge = await _notificationService.GetBadgeAsync(userId);
        return Ok(badge);
    }

    [HttpPatch("seen")]
    public async Task<IActionResult> MarkSeen([FromBody] MarkNotificationsSeenRequest request)
    {
        if (!TryGetUserId(out var userId))
            return Unauthorized();

        await _notificationService.MarkSeenAsync(userId, request.NotificationIds ?? new List<Guid>(), request.MarkAll);
        return NoContent();
    }

    private bool TryGetUserId(out Guid userId)
    {
        userId = Guid.Empty;
        var idClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return !string.IsNullOrWhiteSpace(idClaim) && Guid.TryParse(idClaim, out userId);
    }
}
