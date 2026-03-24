using vision_backend.Application.DTOs.Leaves;
using vision_backend.Domain.Enums;

namespace vision_backend.Application.Interfaces;

public interface ILeaveService
{
    Task<LeaveResponse> ApplyForLeaveAsync(Guid userId, ApplyLeaveRequest request);
    Task<LeaveResponse> ApproveLeaveAsync(Guid leaveId, Guid approverId, UserRole approverRole);
    Task<LeaveResponse> RejectLeaveAsync(Guid leaveId, Guid rejectorId, UserRole rejectorRole, string reason);
    Task<IEnumerable<LeaveResponse>> GetMyLeavesAsync(Guid userId);
    Task<IEnumerable<LeaveResponse>> GetPendingApprovalsAsync(Guid approverId, UserRole approverRole);
    Task<IEnumerable<LeaveResponse>> GetUpcomingLeavesAsync();
    Task<IEnumerable<LeaveTypeResponse>> GetLeaveTypesAsync(bool includeInactive = false);
    Task<LeaveTypeResponse> CreateLeaveTypeAsync(Guid actorId, CreateLeaveTypeRequest request);
    Task<LeaveTypeResponse> UpdateLeaveTypeAsync(Guid actorId, Guid leaveTypeId, UpdateLeaveTypeRequest request);
    Task DeleteLeaveTypeAsync(Guid actorId, Guid leaveTypeId);
    Task<LeaveTypeAssignmentResponse> AssignLeaveTypeAsync(Guid actorId, Guid leaveTypeId, AssignLeaveTypeRequest request);
    Task<AssignLeaveTypeBatchResponse> AssignLeaveTypeBatchAsync(Guid actorId, Guid leaveTypeId, AssignLeaveTypeBatchRequest request);
    Task<AssignLeaveTypeToAllResponse> AssignLeaveTypeToAllUsersAsync(Guid actorId, Guid leaveTypeId, AssignLeaveTypeToAllRequest request);
    Task<LeaveTypeAssignmentResponse> UpdateLeaveTypeAssignmentAsync(Guid actorId, Guid assignmentId, AssignLeaveTypeRequest request);
    Task DeleteLeaveTypeAssignmentAsync(Guid actorId, Guid assignmentId);
    Task<IEnumerable<LeaveTypeAssignmentResponse>> GetLeaveTypeAssignmentsAsync(Guid leaveTypeId);
    Task<IEnumerable<LeaveTypeResponse>> GetMyAssignableLeaveTypesAsync(Guid userId);
    Task<IEnumerable<LeaveBalanceResponse>> GetMyLeaveBalancesAsync(Guid userId, int? year = null);
    Task<IEnumerable<PublicHolidayResponse>> GetPublicHolidaysAsync(DateOnly? fromDate = null, DateOnly? toDate = null, bool includeInactive = false);
    Task<PublicHolidayResponse> CreatePublicHolidayAsync(Guid actorId, CreatePublicHolidayRequest request);
    Task<PublicHolidayResponse> UpdatePublicHolidayAsync(Guid actorId, Guid holidayId, UpdatePublicHolidayRequest request);
    Task DeletePublicHolidayAsync(Guid actorId, Guid holidayId);
}
