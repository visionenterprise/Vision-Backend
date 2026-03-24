namespace vision_backend.Application.DTOs.Leaves;

public class AssignLeaveTypeBatchResponse
{
    public int RequestedCount { get; set; }
    public int EligibleCount { get; set; }
    public int AssignedCount { get; set; }
    public int UpdatedCount { get; set; }
    public int SkippedCount { get; set; }
    public int IneligibleCount { get; set; }
}