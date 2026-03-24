namespace vision_backend.Application.DTOs.Notifications;

public class NotificationBadgeResponse
{
    public int TotalUnseen { get; set; }
    public int RecentUnseen { get; set; }
    public int PendingVoucherApprovals { get; set; }
    public int PendingLeaveApprovals { get; set; }
}