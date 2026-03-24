using System.ComponentModel.DataAnnotations;

namespace vision_backend.Application.DTOs.Leaves;

public class UpdatePublicHolidayRequest
{
    [Required]
    public DateOnly Date { get; set; }

    [Required]
    [MaxLength(120)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Description { get; set; }

    public bool IsActive { get; set; } = true;
}
