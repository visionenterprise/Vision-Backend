using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace vision_backend.Application.DTOs.Leaves;

public class AssignLeaveTypeRequest
{
    [Required]
    public Guid UserId { get; set; }

    [Range(0, 365)]
    [JsonConverter(typeof(NullableIntJsonConverter))]
    public int? AnnualQuotaDaysOverride { get; set; }
}
