namespace vision_backend.Application.DTOs.Dashboard;

public class DashboardSummaryDto
{
    public int TotalEmployees { get; set; }
    public int TotalVouchersThisWeek { get; set; }
    public decimal TotalAmountThisWeek { get; set; }
    
    // Approval stats based on user role
    public int PendingAdminApprovals { get; set; }
    public int PendingSuperAdminApprovals { get; set; }
    public int PendingVoucherApprovalsForMe { get; set; }
    public int PendingLeaveApprovalsForMe { get; set; }
    
    // Analytical stats
    public List<CategoryStatDto> CategoryStats { get; set; } = new();
    public List<MonthlyTrendDto> MonthlyTrends { get; set; } = new();
}

public class MonthlyTrendDto
{
    public string Month { get; set; } = string.Empty;
    public decimal Amount { get; set; }
}
