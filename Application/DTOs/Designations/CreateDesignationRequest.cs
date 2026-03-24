using System.ComponentModel.DataAnnotations;

namespace vision_backend.Application.DTOs.Designations;

public class CreateDesignationRequest
{
    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;
}
