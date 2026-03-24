namespace vision_backend.Application.DTOs.Users;

public class UpdateAdminRolePermissionsRequest
{
    public List<string> PermissionSlugs { get; set; } = new();
}
