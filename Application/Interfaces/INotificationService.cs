using vision_backend.Application.DTOs.Notifications;
using vision_backend.Domain.Enums;

namespace vision_backend.Application.Interfaces;

public interface INotificationService
{
    Task<NotificationResponse> CreateAsync(
        Guid userId,
        string type,
        string title,
        string message,
        string? entityType = null,
        Guid? entityId = null);

    Task<List<NotificationResponse>> GetRecentAsync(Guid userId, int take = 20);
    Task<NotificationBadgeResponse> GetBadgeAsync(Guid userId);
    Task MarkSeenAsync(Guid userId, List<Guid> notificationIds, bool markAll = false);

    Task NotifyLeaveApprovalPendingAsync(Guid leaveId, Guid requesterId, LeaveApprovalLevel nextLevel, Guid? targetPillerId, Guid? owningSuperAdminId);
    Task NotifyVoucherApprovalPendingAsync(Guid voucherId, string voucherNumber, Guid requesterId, LeaveApprovalLevel nextLevel, Guid? targetPillerId, Guid? owningSuperAdminId);
    Task NotifyLeaveDecisionAsync(Guid leaveId, Guid requesterId, LeaveStatus status);
    Task NotifyVoucherDecisionAsync(Guid voucherId, string voucherNumber, Guid requesterId, VoucherStatus status);
}
