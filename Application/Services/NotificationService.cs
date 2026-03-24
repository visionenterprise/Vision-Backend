using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using vision_backend.Application.Constants;
using vision_backend.Application.DTOs.Notifications;
using vision_backend.Application.Interfaces;
using vision_backend.Domain.Entities;
using vision_backend.Domain.Enums;
using vision_backend.Infrastructure.Data;
using vision_backend.Infrastructure.Realtime;

namespace vision_backend.Application.Services;

public class NotificationService : INotificationService
{
    private readonly ApplicationDbContext _context;
    private readonly IHubContext<NotificationsHub> _hubContext;

    public NotificationService(
        ApplicationDbContext context,
        IHubContext<NotificationsHub> hubContext)
    {
        _context = context;
        _hubContext = hubContext;
    }

    public async Task<NotificationResponse> CreateAsync(
        Guid userId,
        string type,
        string title,
        string message,
        string? entityType = null,
        Guid? entityId = null)
    {
        var notification = new Notification
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Type = type,
            Title = title,
            Message = message,
            EntityType = entityType,
            EntityId = entityId,
            IsSeen = false,
            CreatedAt = DateTime.Now,
        };

        _context.Notifications.Add(notification);
        await _context.SaveChangesAsync();

        var response = Map(notification);
        await BroadcastNotificationAsync(userId, response);
        await BroadcastBadgeAsync(userId);
        return response;
    }

    public async Task<List<NotificationResponse>> GetRecentAsync(Guid userId, int take = 20)
    {
        var normalizedTake = Math.Clamp(take, 1, 100);
        var notifications = await _context.Notifications
            .AsNoTracking()
            .Where(n => n.UserId == userId)
            .OrderByDescending(n => n.CreatedAt)
            .Take(normalizedTake)
            .ToListAsync();

        return notifications.Select(Map).ToList();
    }

    public async Task<NotificationBadgeResponse> GetBadgeAsync(Guid userId)
    {
        var totalUnseen = await _context.Notifications
            .AsNoTracking()
            .CountAsync(n => n.UserId == userId && !n.IsSeen);

        var recentUnseen = await _context.Notifications
            .AsNoTracking()
            .Where(n => n.UserId == userId && !n.IsSeen)
            .OrderByDescending(n => n.CreatedAt)
            .Take(10)
            .CountAsync();

        var pendingVoucherApprovals = 0;
        var pendingLeaveApprovals = 0;

        var user = await _context.Users
            .AsNoTracking()
            .Include(u => u.AdminRole)
            .FirstOrDefaultAsync(u => u.Id == userId);
        if (user != null)
        {
            var effectiveRole = EffectiveRoleResolver.GetEffectiveRole(user);

            if (effectiveRole == UserRole.SuperAdmin)
            {
                pendingVoucherApprovals = await _context.Vouchers
                    .AsNoTracking()
                    .CountAsync(v => v.Status == VoucherStatus.PendingSuperAdmin);

                pendingLeaveApprovals = await _context.LeaveRequests
                    .AsNoTracking()
                    .CountAsync(l => l.Status == LeaveStatus.PendingSuperAdmin);
            }
            else if (effectiveRole == UserRole.Admin)
            {
                var hasVoucherApproval = await HasPermissionAsync(userId, PermissionSlugs.VoucherManagement);
                var hasLeaveApproval = await HasPermissionAsync(userId, PermissionSlugs.LeaveApprovals);
                var isAccountsAdmin = user.AdminRole != null
                    && string.Equals(user.AdminRole.Name.Trim(), "account-admin", StringComparison.OrdinalIgnoreCase);

                if (hasVoucherApproval && isAccountsAdmin)
                {
                    pendingVoucherApprovals = await _context.Vouchers
                        .AsNoTracking()
                        .CountAsync(v => v.Status == VoucherStatus.PendingAdmin);
                }

                if (hasLeaveApproval)
                {
                    pendingLeaveApprovals = await _context.LeaveRequests
                        .AsNoTracking()
                        .CountAsync(l => l.Status == LeaveStatus.PendingAdmin);
                }
            }
            else if (effectiveRole == UserRole.Piller)
            {
                var hasVoucherApproval = await HasPermissionAsync(userId, PermissionSlugs.VoucherManagement);
                var hasLeaveApproval = await HasPermissionAsync(userId, PermissionSlugs.LeaveApprovals);

                if (hasVoucherApproval)
                {
                    pendingVoucherApprovals = await _context.Vouchers
                        .AsNoTracking()
                        .CountAsync(v => v.Status == VoucherStatus.PendingPiller && v.TargetPillerId == userId);
                }

                if (hasLeaveApproval)
                {
                    pendingLeaveApprovals = await _context.LeaveRequests
                        .AsNoTracking()
                        .CountAsync(l => l.Status == LeaveStatus.PendingPiller && l.TargetPillerId == userId);
                }
            }
        }

        return new NotificationBadgeResponse
        {
            TotalUnseen = totalUnseen,
            RecentUnseen = recentUnseen,
            PendingVoucherApprovals = pendingVoucherApprovals,
            PendingLeaveApprovals = pendingLeaveApprovals,
        };
    }

    public async Task MarkSeenAsync(Guid userId, List<Guid> notificationIds, bool markAll = false)
    {
        var query = _context.Notifications.Where(n => n.UserId == userId && !n.IsSeen);

        if (!markAll)
        {
            var idSet = notificationIds?.Distinct().ToList() ?? new List<Guid>();
            if (idSet.Count == 0)
                return;

            query = query.Where(n => idSet.Contains(n.Id));
        }

        var now = DateTime.Now;
        await query.ExecuteUpdateAsync(setters => setters
            .SetProperty(n => n.IsSeen, true)
            .SetProperty(n => n.SeenAt, now));

        await BroadcastBadgeAsync(userId);
    }

    public async Task NotifyLeaveApprovalPendingAsync(Guid leaveId, Guid requesterId, LeaveApprovalLevel nextLevel, Guid? targetPillerId, Guid? owningSuperAdminId)
    {
        var requesterName = await GetUserDisplayNameAsync(requesterId);

        if (nextLevel == LeaveApprovalLevel.Piller && targetPillerId.HasValue)
        {
            await CreateAsync(
                targetPillerId.Value,
                "leave_approval_pending",
                "New leave approval request",
                $"{requesterName} submitted a leave request awaiting your approval.",
                "leave",
                leaveId);

            return;
        }

        if (nextLevel == LeaveApprovalLevel.Admin)
        {
            var adminRecipients = await GetLeaveAdminRecipientsAsync();
            foreach (var adminId in adminRecipients)
            {
                await CreateAsync(
                    adminId,
                    "leave_approval_pending",
                    "New leave approval request",
                    $"{requesterName} submitted a leave request awaiting HR/Admin approval.",
                    "leave",
                    leaveId);
            }

            return;
        }

        if (nextLevel == LeaveApprovalLevel.SuperAdmin)
        {
            var recipients = new List<Guid>();
            if (owningSuperAdminId.HasValue)
            {
                recipients.Add(owningSuperAdminId.Value);
            }
            else
            {
                recipients.AddRange(await _context.Users
                    .AsNoTracking()
                    .Where(u => u.Role == UserRole.SuperAdmin)
                    .Select(u => u.Id)
                    .ToListAsync());
            }

            foreach (var superAdminId in recipients.Distinct())
            {
                await CreateAsync(
                    superAdminId,
                    "leave_approval_pending",
                    "Leave request escalated",
                    $"{requesterName}'s leave request awaits your final approval.",
                    "leave",
                    leaveId);
            }
        }
    }

    public async Task NotifyVoucherApprovalPendingAsync(Guid voucherId, string voucherNumber, Guid requesterId, LeaveApprovalLevel nextLevel, Guid? targetPillerId, Guid? owningSuperAdminId)
    {
        var requesterName = await GetUserDisplayNameAsync(requesterId);

        if (nextLevel == LeaveApprovalLevel.Piller && targetPillerId.HasValue)
        {
            await CreateAsync(
                targetPillerId.Value,
                "voucher_approval_pending",
                "New voucher approval request",
                $"{requesterName} submitted voucher {voucherNumber} awaiting your approval.",
                "voucher",
                voucherId);

            return;
        }

        if (nextLevel == LeaveApprovalLevel.Admin)
        {
            var adminRecipients = await GetVoucherAdminRecipientsAsync();
            foreach (var adminId in adminRecipients)
            {
                await CreateAsync(
                    adminId,
                    "voucher_approval_pending",
                    "New voucher approval request",
                    $"{requesterName} submitted voucher {voucherNumber} awaiting Accounts Admin approval.",
                    "voucher",
                    voucherId);
            }

            return;
        }

        if (nextLevel == LeaveApprovalLevel.SuperAdmin)
        {
            var recipients = new List<Guid>();
            if (owningSuperAdminId.HasValue)
            {
                recipients.Add(owningSuperAdminId.Value);
            }
            else
            {
                recipients.AddRange(await _context.Users
                    .AsNoTracking()
                    .Where(u => u.Role == UserRole.SuperAdmin)
                    .Select(u => u.Id)
                    .ToListAsync());
            }

            foreach (var superAdminId in recipients.Distinct())
            {
                await CreateAsync(
                    superAdminId,
                    "voucher_approval_pending",
                    "Voucher request escalated",
                    $"Voucher {voucherNumber} from {requesterName} awaits your final approval.",
                    "voucher",
                    voucherId);
            }
        }
    }

    public async Task NotifyLeaveDecisionAsync(Guid leaveId, Guid requesterId, LeaveStatus status)
    {
        var title = status == LeaveStatus.Approved ? "Leave approved" : "Leave request updated";
        var message = status == LeaveStatus.Approved
            ? "Your leave request has been approved."
            : "Your leave request has been updated. Please check details.";

        if (status == LeaveStatus.Rejected || status == LeaveStatus.RejectedAdmin || status == LeaveStatus.RejectedPiller)
        {
            title = "Leave rejected";
            message = "Your leave request has been rejected.";
        }

        await CreateAsync(requesterId, "leave_decision", title, message, "leave", leaveId);
    }

    public async Task NotifyVoucherDecisionAsync(Guid voucherId, string voucherNumber, Guid requesterId, VoucherStatus status)
    {
        var title = status == VoucherStatus.Approved ? "Voucher approved" : "Voucher request updated";
        var message = status == VoucherStatus.Approved
            ? $"Your voucher {voucherNumber} has been approved."
            : $"Your voucher {voucherNumber} has been updated. Please check details.";

        if (status == VoucherStatus.Rejected || status == VoucherStatus.RejectedAdmin || status == VoucherStatus.RejectedPiller)
        {
            title = "Voucher rejected";
            message = $"Your voucher {voucherNumber} has been rejected.";
        }

        await CreateAsync(requesterId, "voucher_decision", title, message, "voucher", voucherId);
    }

    private async Task<List<Guid>> GetVoucherAdminRecipientsAsync()
    {
        var admins = await _context.Users
            .AsNoTracking()
            .Include(u => u.AdminRole)
            .Where(u => u.Role == UserRole.Admin)
            .ToListAsync();

        var recipients = new List<Guid>();
        foreach (var admin in admins)
        {
            if (admin.AdminRole == null || !string.Equals(admin.AdminRole.Name.Trim(), "account-admin", StringComparison.OrdinalIgnoreCase))
                continue;

            if (await HasPermissionAsync(admin.Id, PermissionSlugs.VoucherManagement))
            {
                recipients.Add(admin.Id);
            }
        }

        return recipients;
    }

    private async Task<List<Guid>> GetLeaveAdminRecipientsAsync()
    {
        var admins = await _context.Users
            .AsNoTracking()
            .Where(u => u.Role == UserRole.Admin)
            .Select(u => u.Id)
            .ToListAsync();

        var recipients = new List<Guid>();
        foreach (var adminId in admins)
        {
            if (await HasPermissionAsync(adminId, PermissionSlugs.LeaveApprovals))
            {
                recipients.Add(adminId);
            }
        }

        return recipients;
    }

    private async Task<string> GetUserDisplayNameAsync(Guid userId)
    {
        var user = await _context.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.FirstName, u.LastName, u.Username })
            .FirstOrDefaultAsync();

        if (user == null)
            return "A user";

        var fullName = $"{user.FirstName} {user.LastName}".Trim();
        return string.IsNullOrWhiteSpace(fullName) ? user.Username : fullName;
    }

    private async Task BroadcastNotificationAsync(Guid userId, NotificationResponse response)
    {
        await _hubContext.Clients.Group(NotificationsHub.GroupNameForUser(userId))
            .SendAsync("notification_received", response);
    }

    private async Task BroadcastBadgeAsync(Guid userId)
    {
        var badge = await GetBadgeAsync(userId);
        await _hubContext.Clients.Group(NotificationsHub.GroupNameForUser(userId))
            .SendAsync("notification_badge_updated", badge);
    }

    private async Task<bool> HasPermissionAsync(Guid userId, string permissionSlug)
    {
        if (!PermissionSlugs.All.Contains(permissionSlug))
            return false;

        var user = await _context.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId);

        if (user == null)
            return false;

        var effectiveRole = EffectiveRoleResolver.GetEffectiveRole(user);
        if (effectiveRole == UserRole.SuperAdmin)
            return true;

        Guid? roleIdForPermissions = null;
        if (effectiveRole == UserRole.Admin)
        {
            roleIdForPermissions = user.AdminRoleId;
        }
        else if (effectiveRole == UserRole.Piller)
        {
            roleIdForPermissions = user.AdminRoleId;
            if (!roleIdForPermissions.HasValue)
            {
                var normalizedPiller = EffectiveRoleResolver.PillerRoleName;
                roleIdForPermissions = await _context.AdminRoles
                    .AsNoTracking()
                    .Where(r => r.Name.ToLower() == normalizedPiller)
                    .OrderBy(r => r.CreatedBy == user.SuperAdminId ? 0 : 1)
                    .ThenBy(r => r.Name)
                    .Select(r => (Guid?)r.Id)
                    .FirstOrDefaultAsync();
            }
        }

        if (!roleIdForPermissions.HasValue)
            return PermissionSlugs.BaselineForAllEmployees.Contains(permissionSlug);

        var rolePermissions = await _context.RolePermissions
            .AsNoTracking()
            .Where(rp => rp.RoleId == roleIdForPermissions.Value)
            .Select(rp => rp.Permission.Slug)
            .ToListAsync();

        return PermissionSlugs.BaselineForAllEmployees.Contains(permissionSlug)
            || rolePermissions.Contains(permissionSlug, StringComparer.Ordinal);
    }

    private static NotificationResponse Map(Notification notification)
    {
        return new NotificationResponse
        {
            Id = notification.Id,
            Type = notification.Type,
            Title = notification.Title,
            Message = notification.Message,
            EntityType = notification.EntityType,
            EntityId = notification.EntityId,
            IsSeen = notification.IsSeen,
            CreatedAt = notification.CreatedAt,
        };
    }
}
