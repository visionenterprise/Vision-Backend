using System.Text.Json.Serialization;

namespace vision_backend.Application.DTOs.Leaves;

public class AssignLeaveTypeToAllRequest
{
	[JsonConverter(typeof(NullableIntJsonConverter))]
    public int? AnnualQuotaDaysOverride { get; set; }
    public bool OverwriteExistingAssignments { get; set; } = false;
}
