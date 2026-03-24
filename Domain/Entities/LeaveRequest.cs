using vision_backend.Domain.Enums;

namespace vision_backend.Domain.Entities;

public class LeaveRequest
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public Guid? LeaveTypeId { get; set; }
    public LeaveType? LeaveType { get; set; }
    public string? LeaveTypeName { get; set; }
    public int RequestedDays { get; set; }
    public int HolidayOverlapDays { get; set; }
    public int DeductedDays { get; set; }
    public int LeaveYear { get; set; }
    public string Reason { get; set; } = string.Empty;
    
    public LeaveStatus Status { get; set; }
    public LeaveApprovalLevel CurrentApprovalLevel { get; set; }
    
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Navigation properties for targeted approver (General User flow)
    public Guid? TargetPillerId { get; set; }
    public User? TargetPiller { get; set; }
    public Guid? OwningSuperAdminId { get; set; }
    public User? OwningSuperAdmin { get; set; }

    // Navigation properties for approvers (optional but good for tracking)
    public Guid? PillerApprovedById { get; set; }
    public User? PillerApprovedBy { get; set; }
    
    public Guid? AdminApprovedById { get; set; }
    public User? AdminApprovedBy { get; set; }
    
    public Guid? SuperAdminApprovedById { get; set; }
    public User? SuperAdminApprovedBy { get; set; }

    public Guid? RejectedById { get; set; }
    public User? RejectedBy { get; set; }

    public string? RejectionReason { get; set; }
}
