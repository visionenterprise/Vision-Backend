namespace vision_backend.Domain.Entities;

public class LeaveTypeAssignment
{
    public Guid Id { get; set; }
    public Guid LeaveTypeId { get; set; }
    public LeaveType LeaveType { get; set; } = null!;

    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    public int? AnnualQuotaDaysOverride { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
