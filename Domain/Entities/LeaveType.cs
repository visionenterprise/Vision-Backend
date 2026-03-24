namespace vision_backend.Domain.Entities;

public class LeaveType
{
	public Guid Id { get; set; }
	public string Name { get; set; } = string.Empty;
	public string? Description { get; set; }
	public bool IsPaid { get; set; }
	public bool IsActive { get; set; } = true;
	public int? AnnualQuotaDays { get; set; }
	public DateTime CreatedAt { get; set; }
	public DateTime UpdatedAt { get; set; }

	public ICollection<LeaveTypeAssignment> Assignments { get; set; } = new List<LeaveTypeAssignment>();
	public ICollection<LeaveRequest> LeaveRequests { get; set; } = new List<LeaveRequest>();
}
