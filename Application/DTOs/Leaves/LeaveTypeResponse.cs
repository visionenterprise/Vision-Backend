namespace vision_backend.Application.DTOs.Leaves;

public class LeaveTypeResponse
{
	public Guid Id { get; set; }
	public string Name { get; set; } = string.Empty;
	public string? Description { get; set; }
	public bool IsPaid { get; set; }
	public bool IsActive { get; set; }
	public int? AnnualQuotaDays { get; set; }
	public DateTime CreatedAt { get; set; }
	public DateTime UpdatedAt { get; set; }
	public int AssignedEmployeesCount { get; set; }
}

