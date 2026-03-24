using vision_backend.Application.DTOs.Users;
using vision_backend.Application.DTOs.Common;
using vision_backend.Domain.Enums;

namespace vision_backend.Application.Interfaces;

public interface IUserService
{
    Task<UserResponse> CreateUserAsync(Guid creatorId, UserRole creatorRole, CreateUserRequest request);
    Task<UserResponse> UpdateProfileAsync(Guid userId, UpdateProfileRequest request);
    Task<UserResponse> UpdateBalanceAsync(Guid userId, decimal amount, string mode, UserRole updaterRole, Guid updaterId);
    Task<UserResponse> GetUserAsync(Guid userId);
    Task<List<UserResponse>> GetAllUsersAsync();
    Task<PagedResult<UserResponse>> GetUsersPagedAsync(UserSearchRequest request);
    Task DeleteUserAsync(Guid userId, Guid deleterId, UserRole deleterRole);
    Task<UserResponse> UploadProfilePictureAsync(Guid userId, Stream fileStream, string fileName, string contentType);
    Task<UserResponse> DeleteProfilePictureAsync(Guid userId);
    Task<UserResponse> UpdateUserRoleAsync(Guid actorUserId, Guid userId, UserRole role, Guid? adminRoleId);
    Task AdminResetPasswordAsync(Guid userId, string newPassword);
    Task<List<string>> GetEffectivePermissionsAsync(Guid userId);
    Task<bool> HasPermissionAsync(Guid userId, string permissionSlug);
    Task<List<AdminRoleResponse>> GetAdminRolesAsync(Guid superAdminId);
    Task<AdminRoleResponse> CreateAdminRoleAsync(Guid superAdminId, CreateAdminRoleRequest request);
    Task<AdminRoleResponse> UpdateAdminRoleAsync(Guid superAdminId, Guid roleId, UpdateAdminRoleRequest request);
    Task<AdminRoleResponse> UpdateAdminRolePermissionsAsync(Guid superAdminId, Guid roleId, List<string> permissionSlugs);
    Task DeleteAdminRoleAsync(Guid actorUserId, Guid roleId);
    Task<UserResponse> AssignAdminRoleAsync(Guid adminUserId, Guid roleId);
    Task<UserResponse> AssignPillerToSuperAdminAsync(Guid pillerId, Guid? superAdminId);
}
