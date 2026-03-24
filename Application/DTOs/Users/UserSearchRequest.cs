using vision_backend.Domain.Enums;

namespace vision_backend.Application.DTOs.Users;

public class UserSearchRequest
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
    public string? SearchTerm { get; set; }
    public string? SortColumn { get; set; }
    public string? SortOrder { get; set; } // "asc" or "desc"
    public UserRole? RoleFilter { get; set; }
    public Guid? ExcludeUserId { get; set; }
}
