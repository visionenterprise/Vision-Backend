using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace vision_backend.Application.DTOs.Leaves;

public class UpdateLeaveTypeRequest
{
    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Description { get; set; }

    public bool IsPaid { get; set; } = true;
    public bool IsActive { get; set; } = true;

    [Range(0, 365)]
    [JsonConverter(typeof(NullableIntJsonConverter))]
    public int? AnnualQuotaDays { get; set; }
}
