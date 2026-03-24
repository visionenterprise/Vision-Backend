namespace vision_backend.Application.DTOs.Notifications;

public class MarkNotificationsSeenRequest
{
    public List<Guid> NotificationIds { get; set; } = new();
    public bool MarkAll { get; set; }
}