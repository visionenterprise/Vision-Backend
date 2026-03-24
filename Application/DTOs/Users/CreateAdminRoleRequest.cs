using System.ComponentModel.DataAnnotations;

namespace vision_backend.Application.DTOs.Users;

public class CreateAdminRoleRequest
{
    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    public List<string> PermissionSlugs { get; set; } = new();
}
