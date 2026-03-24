using vision_backend.Application.DTOs.Users;

namespace vision_backend.Application.DTOs.Leaves;

public class LeaveTypeAssignmentResponse
{
    public Guid Id { get; set; }
    public Guid LeaveTypeId { get; set; }
    public string LeaveTypeName { get; set; } = string.Empty;
    public Guid UserId { get; set; }
    public UserResponse User { get; set; } = null!;
    public int? AnnualQuotaDaysOverride { get; set; }
    public int? EffectiveAnnualQuotaDays { get; set; }
    public int UsedDays { get; set; }
    public int RemainingDays { get; set; }
    public int Year { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
