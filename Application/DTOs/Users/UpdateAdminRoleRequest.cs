using System.ComponentModel.DataAnnotations;

namespace vision_backend.Application.DTOs.Users;

public class UpdateAdminRoleRequest
{
    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;
}
