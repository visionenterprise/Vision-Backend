using vision_backend.Domain.Enums;
using vision_backend.Application.DTOs.Users;

namespace vision_backend.Application.DTOs.Leaves;

public class LeaveResponse
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public UserResponse User { get; set; } = null!;
    
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public Guid? LeaveTypeId { get; set; }
    public string? LeaveTypeName { get; set; }
    public int RequestedDays { get; set; }
    public int HolidayOverlapDays { get; set; }
    public int DeductedDays { get; set; }
    public int LeaveYear { get; set; }
    public string Reason { get; set; } = string.Empty;
    
    public LeaveStatus Status { get; set; }
    public LeaveApprovalLevel CurrentApprovalLevel { get; set; }
    
    public DateTime CreatedAt { get; set; }

    public string? RejectionReason { get; set; }
    
    public Guid? TargetPillerId { get; set; }
    public string? TargetPillerName { get; set; }
    public Guid? OwningSuperAdminId { get; set; }
    public string? OwningSuperAdminName { get; set; }
    
    public Guid? PillerApprovedById { get; set; }
    public string? PillerApprovedByName { get; set; }
    public Guid? AdminApprovedById { get; set; }
    public string? AdminApprovedByName { get; set; }
    public Guid? SuperAdminApprovedById { get; set; }
    public string? SuperAdminApprovedByName { get; set; }
    public Guid? RejectedById { get; set; }
    public string? RejectedByName { get; set; }
}
