namespace vision_backend.Application.DTOs.Leaves;

public class LeaveBalanceResponse
{
    public Guid LeaveTypeId { get; set; }
    public string LeaveTypeName { get; set; } = string.Empty;
    public bool IsPaid { get; set; }
    public int? AnnualQuotaDays { get; set; }
    public int UsedDays { get; set; }
    public int RemainingDays { get; set; }
    public int Year { get; set; }
}
