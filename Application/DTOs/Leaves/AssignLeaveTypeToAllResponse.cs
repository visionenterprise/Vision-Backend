namespace vision_backend.Application.DTOs.Leaves;

public class AssignLeaveTypeToAllResponse
{
    public int TotalEligibleUsers { get; set; }
    public int AssignedCount { get; set; }
    public int UpdatedCount { get; set; }
    public int SkippedCount { get; set; }
}
