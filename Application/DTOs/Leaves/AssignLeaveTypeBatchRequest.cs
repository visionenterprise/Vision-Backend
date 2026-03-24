using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace vision_backend.Application.DTOs.Leaves;

public class AssignLeaveTypeBatchRequest
{
    [Required]
    [MinLength(1)]
    public List<Guid> UserIds { get; set; } = [];

    [Range(0, 365)]
    [JsonConverter(typeof(NullableIntJsonConverter))]
    public int? AnnualQuotaDaysOverride { get; set; }

    public bool OverwriteExistingAssignments { get; set; } = false;
}