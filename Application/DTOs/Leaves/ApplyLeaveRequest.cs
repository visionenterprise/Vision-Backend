using System.ComponentModel.DataAnnotations;

namespace vision_backend.Application.DTOs.Leaves;

public class ApplyLeaveRequest
{
    [Required]
    public Guid LeaveTypeId { get; set; }

    [Required]
    public DateOnly StartDate { get; set; }

    [Required]
    public DateOnly EndDate { get; set; }

    [Required]
    [MaxLength(500)]
    public string Reason { get; set; } = string.Empty;

    public Guid? TargetPillerId { get; set; }
}
