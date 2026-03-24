namespace vision_backend.Application.DTOs.Users;

public class AdminRoleResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid CreatedBy { get; set; }
    public List<string> PermissionSlugs { get; set; } = new();
}
