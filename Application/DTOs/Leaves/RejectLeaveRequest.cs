using System.ComponentModel.DataAnnotations;

namespace vision_backend.Application.DTOs.Leaves;

public class RejectLeaveRequest
{
    [Required]
    [MaxLength(500)]
    public string Reason { get; set; } = string.Empty;
}
