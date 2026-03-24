using Microsoft.EntityFrameworkCore;
using vision_backend.Domain.Entities;
using vision_backend.Domain.Enums;
using vision_backend.Infrastructure.Data;

namespace vision_backend.Infrastructure.Repositories;

public class LeaveRequestRepository : ILeaveRequestRepository
{
    private readonly ApplicationDbContext _context;

    public LeaveRequestRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<LeaveRequest?> GetByIdAsync(Guid id)
    {
        return await _context.LeaveRequests
            .Include(l => l.User)
            .Include(l => l.LeaveType)
            .Include(l => l.TargetPiller)
            .Include(l => l.OwningSuperAdmin)
            .Include(l => l.PillerApprovedBy)
            .Include(l => l.AdminApprovedBy)
            .Include(l => l.SuperAdminApprovedBy)
            .Include(l => l.RejectedBy)
            .FirstOrDefaultAsync(l => l.Id == id);
    }

    public async Task<IEnumerable<LeaveRequest>> GetByUserIdAsync(Guid userId)
    {
        return await _context.LeaveRequests
            .Include(l => l.User)
            .Include(l => l.LeaveType)
            .Include(l => l.TargetPiller)
            .Include(l => l.OwningSuperAdmin)
            .Include(l => l.PillerApprovedBy)
            .Include(l => l.AdminApprovedBy)
            .Include(l => l.SuperAdminApprovedBy)
            .Include(l => l.RejectedBy)
            .Where(l => l.UserId == userId)
            .OrderByDescending(l => l.CreatedAt)
            .ToListAsync();
    }

    public async Task<IEnumerable<LeaveRequest>> GetPendingByApprovalLevelAsync(
        LeaveApprovalLevel level,
        Guid? targetPillerId = null,
        Guid? owningSuperAdminId = null,
        bool includeDirectSelfRequestsForPiller = false,
        bool includeApprovalHistory = false,
        bool adminSharedApprovalAccess = false)
    {
        var query = _context.LeaveRequests
            .Include(l => l.User)
            .Include(l => l.LeaveType)
            .Include(l => l.TargetPiller)
            .Include(l => l.OwningSuperAdmin)
            .Include(l => l.PillerApprovedBy)
            .Include(l => l.AdminApprovedBy)
            .Include(l => l.SuperAdminApprovedBy)
            .Include(l => l.RejectedBy)
            .AsQueryable();

        if (includeApprovalHistory)
        {
            if (level == LeaveApprovalLevel.Piller)
            {
                query = query.Where(l =>
                    l.CurrentApprovalLevel == LeaveApprovalLevel.Piller ||
                    l.PillerApprovedById != null ||
                    l.Status == LeaveStatus.RejectedPiller);
            }
            else if (adminSharedApprovalAccess)
            {
                query = query.Where(l =>
                    l.Status != LeaveStatus.PendingPiller &&
                    l.Status != LeaveStatus.RejectedPiller);
            }
            else if (level == LeaveApprovalLevel.SuperAdmin)
            {
                query = query.Where(l =>
                    l.CurrentApprovalLevel == LeaveApprovalLevel.SuperAdmin ||
                    l.SuperAdminApprovedById != null ||
                    l.Status == LeaveStatus.Rejected);
            }
            else
            {
                query = query.Where(l =>
                    l.CurrentApprovalLevel == level ||
                    l.PillerApprovedById != null ||
                    l.AdminApprovedById != null ||
                    l.SuperAdminApprovedById != null ||
                    l.RejectedById != null);
            }
        }
        else
        {
            query = query.Where(l =>
                l.CurrentApprovalLevel == level &&
                l.Status != LeaveStatus.Rejected &&
                l.Status != LeaveStatus.RejectedAdmin &&
                l.Status != LeaveStatus.RejectedPiller &&
                l.Status != LeaveStatus.Approved);
        }

        if (targetPillerId.HasValue)
        {
            if (includeDirectSelfRequestsForPiller)
            {
                query = query.Where(l => l.TargetPillerId == targetPillerId.Value || l.UserId == targetPillerId.Value);
            }
            else
            {
                query = query.Where(l => l.TargetPillerId == targetPillerId.Value);
            }
        }

        if (owningSuperAdminId.HasValue)
        {
            query = query.Where(l => l.OwningSuperAdminId == owningSuperAdminId.Value);
        }

        if (includeApprovalHistory)
        {
            query = level switch
            {
                LeaveApprovalLevel.Piller => query
                    .OrderBy(l => l.Status == LeaveStatus.PendingPiller ? 0 : 1)
                    .ThenByDescending(l => l.CreatedAt),
                LeaveApprovalLevel.Admin => query
                    .OrderBy(l => l.Status == LeaveStatus.PendingAdmin ? 0 : 1)
                    .ThenByDescending(l => l.CreatedAt),
                LeaveApprovalLevel.SuperAdmin => query
                    .OrderBy(l => l.Status == LeaveStatus.PendingSuperAdmin ? 0 : 1)
                    .ThenByDescending(l => l.CreatedAt),
                _ => query.OrderByDescending(l => l.CreatedAt)
            };
        }
        else
        {
            query = query.OrderByDescending(l => l.CreatedAt);
        }

        return await query.ToListAsync();
    }

    public async Task<IEnumerable<LeaveRequest>> GetUpcomingApprovedLeavesAsync(DateTime date)
    {
        var targetDate = DateOnly.FromDateTime(date);
        return await _context.LeaveRequests
            .Include(l => l.User)
            .Include(l => l.LeaveType)
            .Include(l => l.TargetPiller)
            .Include(l => l.OwningSuperAdmin)
            .Include(l => l.PillerApprovedBy)
            .Include(l => l.AdminApprovedBy)
            .Include(l => l.SuperAdminApprovedBy)
            .Include(l => l.RejectedBy)
            .Where(l => l.Status == LeaveStatus.Approved && l.StartDate >= targetDate)
            .OrderBy(l => l.StartDate)
            .ToListAsync();
    }

    public async Task<LeaveRequest> AddAsync(LeaveRequest leaveRequest)
    {
        await _context.LeaveRequests.AddAsync(leaveRequest);
        await _context.SaveChangesAsync();
        return leaveRequest;
    }

    public async Task UpdateAsync(LeaveRequest leaveRequest)
    {
        _context.LeaveRequests.Update(leaveRequest);
        await _context.SaveChangesAsync();
    }
}
