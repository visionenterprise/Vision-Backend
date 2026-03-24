using vision_backend.Domain.Entities;
using vision_backend.Domain.Enums;

namespace vision_backend.Infrastructure.Repositories;

public interface ILeaveRequestRepository
{
    Task<LeaveRequest?> GetByIdAsync(Guid id);
    Task<IEnumerable<LeaveRequest>> GetByUserIdAsync(Guid userId);
    Task<IEnumerable<LeaveRequest>> GetPendingByApprovalLevelAsync(
        LeaveApprovalLevel level,
        Guid? targetPillerId = null,
        Guid? owningSuperAdminId = null,
        bool includeDirectSelfRequestsForPiller = false,
        bool includeApprovalHistory = false,
        bool adminSharedApprovalAccess = false);
    Task<IEnumerable<LeaveRequest>> GetUpcomingApprovedLeavesAsync(DateTime date);
    Task<LeaveRequest> AddAsync(LeaveRequest leaveRequest);
    Task UpdateAsync(LeaveRequest leaveRequest);
}
