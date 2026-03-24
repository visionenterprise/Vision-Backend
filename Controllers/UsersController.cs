using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using vision_backend.Application.DTOs.Users;
using vision_backend.Application.DTOs.Common;
using vision_backend.Application.Constants;
using vision_backend.Application.Interfaces;
using vision_backend.Domain.Enums;

namespace vision_backend.Controllers;

[ApiController]
[Route("api/users")]
public class UsersController : ControllerBase
{
    private readonly IUserService _userService;

    public UsersController(IUserService userService)
    {
        _userService = userService;
    }

    [Authorize]
    [HttpPost]
    public async Task<ActionResult<UserResponse>> CreateUser([FromBody] CreateUserRequest request)
    {
        if (!TryGetUserId(out var creatorId) || !TryGetRole(out var role))
        {
            return Unauthorized();
        }

        if (!await EnsureUserManagementPermissionAsync())
            return Forbid();

        try
        {
            var response = await _userService.CreateUserAsync(creatorId, role, request);
            return Ok(response);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [Authorize(Roles = "SuperAdmin,Admin,Piller")]
    [HttpGet]
    public async Task<ActionResult<List<UserResponse>>> GetAll()
    {
        if (!await EnsureRoleOrUserManagementOrVoucherManagementPermissionAsync())
            return Forbid();

        var users = await _userService.GetAllUsersAsync();
        
        if (TryGetUserId(out var currentUserId))
        {
            users = users.Where(u => u.Id != currentUserId).ToList();
        }
        
        return Ok(users);
    }

    [Authorize]
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<UserResponse>> GetById(Guid id)
    {
        if (!await EnsureUserManagementPermissionAsync())
            return Forbid();

        try
        {
            var user = await _userService.GetUserAsync(id);
            return Ok(user);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [Authorize]
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<UserResponse>> UpdateProfile(Guid id, [FromBody] UpdateProfileRequest request)
    {
        if (!TryGetUserId(out var currentUserId) || !TryGetRole(out var role))
        {
            return Unauthorized();
        }

        if (currentUserId != id && role != UserRole.SuperAdmin && role != UserRole.Admin)
        {
            return Forbid();
        }

        if (currentUserId != id && !await EnsureUserManagementPermissionAsync())
            return Forbid();

        try
        {
            var user = await _userService.UpdateProfileAsync(id, request);
            return Ok(user);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [Authorize(Roles = "SuperAdmin,Admin")]
    [HttpPatch("{id:guid}/balance")]
    public async Task<ActionResult<UserResponse>> UpdateBalance(Guid id, [FromBody] UpdateBalanceRequest request)
    {
        if (!await EnsureUserManagementPermissionAsync())
            return Forbid();

        if (!TryGetUserId(out var updaterId))
        {
            return Forbid();
        }

        if (!TryGetRole(out var role))
        {
            return Forbid();
        }

        try
        {
            var user = await _userService.UpdateBalanceAsync(id, request.Amount, request.Mode, role, updaterId);
            return Ok(user);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [Authorize(Roles = "SuperAdmin,Admin")]
    [HttpDelete("{id:guid}")]
    public async Task<ActionResult> DeleteUser(Guid id)
    {
        if (!await EnsureUserManagementPermissionAsync())
            return Forbid();

        if (!TryGetUserId(out var deleterId))
            return Forbid();

        if (!TryGetRole(out var role)) return Forbid();

        try
        {
            await _userService.DeleteUserAsync(id, deleterId, role);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [Authorize]
    [HttpGet("search")]
    public async Task<ActionResult<PagedResult<UserResponse>>> GetUsersPaged([FromQuery] UserSearchRequest request)
    {
        if (!TryGetRole(out var currentRole))
            return Unauthorized();

        var isGeneralUserPillerLookup = currentRole == UserRole.GeneralUser
            && (!request.RoleFilter.HasValue || request.RoleFilter == UserRole.Piller);

        if (!isGeneralUserPillerLookup && !await EnsureUserManagementOrVoucherManagementPermissionAsync())
            return Forbid();

        if (TryGetUserId(out var currentUserId))
        {
            request.ExcludeUserId = currentUserId;
        }

        if (currentRole == UserRole.GeneralUser)
        {
            // General Users can only search for Pillers (for leave applications)
            request.RoleFilter = UserRole.Piller;
        }

        var result = await _userService.GetUsersPagedAsync(request);
        return Ok(result);
    }

    [Authorize]
    [HttpPost("{id:guid}/profile-picture")]
    [Consumes("multipart/form-data")]
    public async Task<ActionResult<UserResponse>> UploadProfilePicture(Guid id, IFormFile file)
    {
        if (!TryGetUserId(out var currentUserId) || !TryGetRole(out var role))
        {
            return Unauthorized();
        }

        if (currentUserId != id && role != UserRole.SuperAdmin && role != UserRole.Admin)
        {
            return Forbid();
        }

        if (file == null || file.Length == 0)
        {
            return BadRequest("No file uploaded.");
        }

        try
        {
            await using var stream = file.OpenReadStream();
            var response = await _userService.UploadProfilePictureAsync(id, stream, file.FileName, file.ContentType);
            return Ok(response);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [Authorize]
    [HttpDelete("{id:guid}/profile-picture")]
    public async Task<ActionResult<UserResponse>> DeleteProfilePicture(Guid id)
    {
        if (!TryGetUserId(out var currentUserId) || !TryGetRole(out var role))
        {
            return Unauthorized();
        }

        if (currentUserId != id && role != UserRole.SuperAdmin && role != UserRole.Admin)
        {
            return Forbid();
        }

        try
        {
            var response = await _userService.DeleteProfilePictureAsync(id);
            return Ok(response);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [Authorize]
    [HttpPost("{id:guid}/reset-password")]
    public async Task<IActionResult> AdminResetPassword(Guid id, [FromBody] AdminResetPasswordRequest request)
    {
        if (!await EnsureUserManagementPermissionAsync())
            return Forbid();

        if (!TryGetRole(out var role))
        {
            return Unauthorized();
        }

        if (role != UserRole.SuperAdmin && role != UserRole.Admin)
        {
            return Forbid();
        }

        try
        {
            await _userService.AdminResetPasswordAsync(id, request.NewPassword);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, ex.Message);
        }
    }

    private bool TryGetRole(out UserRole role)
    {
        role = UserRole.GeneralUser;
        var roleClaim = User.FindFirstValue(ClaimTypes.Role);
        return !string.IsNullOrWhiteSpace(roleClaim) && Enum.TryParse(roleClaim, out role);
    }

    private bool TryGetUserId(out Guid userId)
    {
        userId = Guid.Empty;
        var idClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return !string.IsNullOrWhiteSpace(idClaim) && Guid.TryParse(idClaim, out userId);
    }

    [Authorize]
    [HttpGet("roles")]
    public async Task<ActionResult<List<AdminRoleResponse>>> GetAdminRoles()
    {
        if (!TryGetUserId(out var actorId))
            return Unauthorized();

        if (!await EnsureRoleOrUserManagementPermissionAsync())
            return Forbid();

        var roles = await _userService.GetAdminRolesAsync(actorId);
        return Ok(roles);
    }

    [Authorize]
    [HttpPost("roles")]
    public async Task<ActionResult<AdminRoleResponse>> CreateAdminRole([FromBody] CreateAdminRoleRequest request)
    {
        if (!TryGetUserId(out var actorId))
            return Unauthorized();

        if (!await EnsureRoleManagementPermissionAsync())
            return Forbid();

        try
        {
            var role = await _userService.CreateAdminRoleAsync(actorId, request);
            return Ok(role);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [Authorize]
    [HttpPut("roles/{roleId:guid}")]
    public async Task<ActionResult<AdminRoleResponse>> UpdateAdminRole(Guid roleId, [FromBody] UpdateAdminRoleRequest request)
    {
        if (!TryGetUserId(out var actorId))
            return Unauthorized();

        if (!await EnsureRoleManagementPermissionAsync())
            return Forbid();

        try
        {
            var role = await _userService.UpdateAdminRoleAsync(actorId, roleId, request);
            return Ok(role);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [Authorize]
    [HttpPut("roles/{roleId:guid}/permissions")]
    public async Task<ActionResult<AdminRoleResponse>> UpdateAdminRolePermissions(Guid roleId, [FromBody] UpdateAdminRolePermissionsRequest request)
    {
        if (!TryGetUserId(out var actorId))
            return Unauthorized();

        if (!await EnsureRoleManagementPermissionAsync())
            return Forbid();

        try
        {
            var role = await _userService.UpdateAdminRolePermissionsAsync(actorId, roleId, request.PermissionSlugs ?? new List<string>());
            return Ok(role);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [Authorize]
    [HttpDelete("roles/{roleId:guid}")]
    public async Task<ActionResult> DeleteAdminRole(Guid roleId)
    {
        if (!TryGetUserId(out var actorId))
            return Unauthorized();

        if (!await EnsureRoleManagementPermissionAsync())
            return Forbid();

        try
        {
            await _userService.DeleteAdminRoleAsync(actorId, roleId);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [Authorize]
    [HttpPatch("{id:guid}/assign-admin-role")]
    public async Task<ActionResult<UserResponse>> AssignAdminRole(Guid id, [FromBody] AssignAdminRoleRequest request)
    {
        if (!await EnsureRoleManagementPermissionAsync())
            return Forbid();

        try
        {
            var user = await _userService.AssignAdminRoleAsync(id, request.RoleId);
            return Ok(user);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [Authorize]
    [HttpPatch("{id:guid}/role")]
    public async Task<ActionResult<UserResponse>> UpdateUserRole(Guid id, [FromBody] UpdateUserRoleRequest request)
    {
        if (!TryGetUserId(out var actorId))
            return Unauthorized();

        if (!await EnsureUserManagementPermissionAsync())
            return Forbid();

        try
        {
            var user = await _userService.UpdateUserRoleAsync(actorId, id, request.Role, request.AdminRoleId);
            return Ok(user);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [Authorize]
    [HttpPatch("{pillerId:guid}/assign-superadmin")]
    public async Task<ActionResult<UserResponse>> AssignPillerOwner(Guid pillerId, [FromBody] AssignPillerOwnerRequest request)
    {
        if (!TryGetUserId(out _))
            return Unauthorized();

        if (!await EnsureRoleManagementPermissionAsync())
            return Forbid();

        try
        {
            var user = await _userService.AssignPillerToSuperAdminAsync(pillerId, request.SuperAdminId);
            return Ok(user);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    private async Task<bool> EnsureUserManagementPermissionAsync()
    {
        if (!TryGetUserId(out var userId) || !TryGetRole(out var role))
            return false;

        if (role == UserRole.SuperAdmin)
            return true;

        return await _userService.HasPermissionAsync(userId, PermissionSlugs.UserManagement);
    }

    private async Task<bool> EnsureRoleManagementPermissionAsync()
    {
        if (!TryGetUserId(out var userId) || !TryGetRole(out var role))
            return false;

        if (role == UserRole.SuperAdmin)
            return true;

        if (role != UserRole.Admin)
            return false;

        return await _userService.HasPermissionAsync(userId, PermissionSlugs.RoleManagement);
    }

    private async Task<bool> EnsureRoleOrUserManagementPermissionAsync()
    {
        if (!TryGetUserId(out var userId) || !TryGetRole(out var role))
            return false;

        if (role == UserRole.SuperAdmin)
            return true;

        var hasRoleManagement = await _userService.HasPermissionAsync(userId, PermissionSlugs.RoleManagement);
        if (hasRoleManagement)
            return true;

        return await _userService.HasPermissionAsync(userId, PermissionSlugs.UserManagement);
    }

    private async Task<bool> EnsureUserManagementOrVoucherManagementPermissionAsync()
    {
        if (!TryGetUserId(out var userId) || !TryGetRole(out var role))
            return false;

        if (role == UserRole.SuperAdmin)
            return true;

        var hasUserManagement = await _userService.HasPermissionAsync(userId, PermissionSlugs.UserManagement);
        if (hasUserManagement)
            return true;

        if (role == UserRole.Admin)
        {
            var hasVoucherManagement = await _userService.HasPermissionAsync(userId, PermissionSlugs.VoucherManagement);
            if (hasVoucherManagement)
                return true;

            return await _userService.HasPermissionAsync(userId, PermissionSlugs.LeaveManagement);
        }

        return false;
    }

    private async Task<bool> EnsureRoleOrUserManagementOrVoucherManagementPermissionAsync()
    {
        if (await EnsureRoleOrUserManagementPermissionAsync())
            return true;

        if (!TryGetUserId(out var userId) || !TryGetRole(out var role))
            return false;

        if (role != UserRole.Admin)
            return false;

        var hasVoucherManagement = await _userService.HasPermissionAsync(userId, PermissionSlugs.VoucherManagement);
        if (hasVoucherManagement)
            return true;

        return await _userService.HasPermissionAsync(userId, PermissionSlugs.LeaveManagement);
    }
}
