using Microsoft.EntityFrameworkCore;
using vision_backend.Application.Constants;
using vision_backend.Application.DTOs.Dashboard;
using vision_backend.Application.Interfaces;
using vision_backend.Domain.Enums;
using vision_backend.Infrastructure.Data;

namespace vision_backend.Application.Services;

public class DashboardService : IDashboardService
{
    private readonly ApplicationDbContext _context;
    private readonly IUserService _userService;

    public DashboardService(ApplicationDbContext context, IUserService userService)
    {
        _context = context;
        _userService = userService;
    }

    public async Task<DashboardSummaryDto> GetDashboardSummaryAsync(Guid userId, UserRole role)
    {
        var now = DateTime.Now;
        var sevenDaysAgo = now.AddDays(-7).Date;
        var currentUser = await _context.Users
            .Include(u => u.AdminRole)
            .FirstOrDefaultAsync(u => u.Id == userId);

        var effectiveRole = currentUser != null
            ? EffectiveRoleResolver.GetEffectiveRole(currentUser)
            : role;

        // Total Employees
        var totalEmployees = await _context.Users.CountAsync();

        // Vouchers this week (last 7 days)
        var weeklyVouchers = await _context.Vouchers
            .Where(v => v.CreatedAt >= sevenDaysAgo)
            .ToListAsync();

        var totalVouchersThisWeek = weeklyVouchers.Count;
        var totalAmountThisWeek = weeklyVouchers.Sum(v => v.Amount);

        // Pending Approvals specific to role
        int pendingAdminApprovals = 0;
        int pendingSuperAdminApprovals = 0;
        int pendingVoucherApprovalsForMe = 0;
        int pendingLeaveApprovalsForMe = 0;

        if (effectiveRole == UserRole.Admin || effectiveRole == UserRole.SuperAdmin)
        {
            // Awaiting admin review
            pendingAdminApprovals = await _context.Vouchers
                .CountAsync(v => v.Status == VoucherStatus.PendingAdmin || v.Status == VoucherStatus.ApprovedPiller);

            // Awaiting super-admin review
            pendingSuperAdminApprovals = await _context.Vouchers
                .CountAsync(v => v.Status == VoucherStatus.PendingSuperAdmin || v.Status == VoucherStatus.ApprovedAdmin);
        }

        if (effectiveRole == UserRole.Piller)
        {
            pendingVoucherApprovalsForMe = await _context.Vouchers
                .CountAsync(v => v.Status == VoucherStatus.PendingPiller && v.TargetPillerId == userId);

            pendingLeaveApprovalsForMe = await _context.LeaveRequests
                .CountAsync(l => l.Status == LeaveStatus.PendingPiller && l.TargetPillerId == userId);
        }
        else if (effectiveRole == UserRole.Admin)
        {
            var hasVoucherPermission = await _userService.HasPermissionAsync(userId, PermissionSlugs.VoucherManagement);
            var hasLeavePermission = await _userService.HasPermissionAsync(userId, PermissionSlugs.LeaveApprovals);
            var isAccountsAdmin = currentUser?.AdminRole != null
                && string.Equals(currentUser.AdminRole.Name.Trim(), "account-admin", StringComparison.OrdinalIgnoreCase);

            if (hasVoucherPermission && isAccountsAdmin)
            {
                pendingVoucherApprovalsForMe = await _context.Vouchers
                    .CountAsync(v => v.Status == VoucherStatus.PendingAdmin);
            }

            if (hasLeavePermission)
            {
                pendingLeaveApprovalsForMe = await _context.LeaveRequests
                    .CountAsync(l => l.Status == LeaveStatus.PendingAdmin);
            }
        }
        else if (effectiveRole == UserRole.SuperAdmin)
        {
            pendingVoucherApprovalsForMe = await _context.Vouchers
                .CountAsync(v => v.Status == VoucherStatus.PendingSuperAdmin);

            pendingLeaveApprovalsForMe = await _context.LeaveRequests
                .CountAsync(l => l.Status == LeaveStatus.PendingSuperAdmin);
        }

        // Category Stats (All time, all non-rejected vouchers)
        // Project to anonymous type with the raw enum int — no string operations in SQL
        var categoryRaw = await _context.Vouchers
            .Where(v => v.Status != VoucherStatus.Rejected)
            .GroupBy(v => v.Category)
            .Select(g => new
            {
                Category = g.Key,          // enum (int) — translatable
                Amount = g.Sum(v => v.Amount),
                Count  = g.Count()
            })
            .OrderByDescending(s => s.Amount)
            .ToListAsync();

        // Convert enum to name string in C# (not SQL)
        var categoryStats = categoryRaw
            .Select(g => new CategoryStatDto
            {
                Category = g.Category.ToString(),
                Amount   = g.Amount,
                Count    = g.Count
            })
            .ToList();

        // Monthly Trends (Last 6 months, all submitted non-rejected vouchers)
        var sixMonthsAgo = new DateTime(now.Year, now.Month, 1).AddMonths(-5);

        // Project to anonymous type with raw ints — string.Format is not translatable to SQL
        var trendsRaw = await _context.Vouchers
            .Where(v => v.CreatedAt >= sixMonthsAgo && v.Status != VoucherStatus.Rejected)
            .GroupBy(v => new { v.CreatedAt.Year, v.CreatedAt.Month })
            .Select(g => new
            {
                g.Key.Year,
                g.Key.Month,
                Amount = g.Sum(v => v.Amount)
            })
            .OrderBy(t => t.Year)
            .ThenBy(t => t.Month)
            .ToListAsync();

        // Format the month string in C# after materialization
        var monthlyTrends = trendsRaw
            .Select(t => new MonthlyTrendDto
            {
                Month  = $"{t.Year}-{t.Month:D2}",
                Amount = t.Amount
            })
            .ToList();

        return new DashboardSummaryDto
        {
            TotalEmployees = totalEmployees,
            TotalVouchersThisWeek = totalVouchersThisWeek,
            TotalAmountThisWeek = totalAmountThisWeek,
            PendingAdminApprovals = pendingAdminApprovals,
            PendingSuperAdminApprovals = pendingSuperAdminApprovals,
            PendingVoucherApprovalsForMe = pendingVoucherApprovalsForMe,
            PendingLeaveApprovalsForMe = pendingLeaveApprovalsForMe,
            CategoryStats = categoryStats,
            MonthlyTrends = monthlyTrends
        };
    }
}
