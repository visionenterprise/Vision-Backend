using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using vision_backend.Application.Constants;
using vision_backend.Application.DTOs.Designations;
using vision_backend.Application.Interfaces;
using vision_backend.Domain.Enums;

namespace vision_backend.Controllers;

[ApiController]
[Route("api/designations")]
[Authorize]
public class DesignationsController : ControllerBase
{
    private readonly IDesignationService _designationService;
    private readonly IUserService _userService;

    public DesignationsController(IDesignationService designationService, IUserService userService)
    {
        _designationService = designationService;
        _userService = userService;
    }

    /// <summary>
    /// Get all active designations. Available to all authenticated users (for dropdown in forms).
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<List<DesignationResponse>>> GetAll()
    {
        var designations = await _designationService.GetAllDesignationsAsync();
        return Ok(designations);
    }

    /// <summary>
    /// Get a single designation by ID.
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<DesignationResponse>> GetById(Guid id)
    {
        try
        {
            var designation = await _designationService.GetDesignationAsync(id);
            return Ok(designation);
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(ex.Message);
        }
    }

    /// <summary>
    /// Create a new designation. Admin and SuperAdmin only.
    /// </summary>
    [HttpPost]
    [Authorize(Roles = "SuperAdmin,Admin")]
    public async Task<ActionResult<DesignationResponse>> Create([FromBody] CreateDesignationRequest request)
    {
        if (!await EnsureUserManagementPermissionAsync())
            return Forbid();

        try
        {
            var designation = await _designationService.CreateDesignationAsync(request);
            return Ok(designation);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    /// <summary>
    /// Update an existing designation. Admin and SuperAdmin only.
    /// </summary>
    [HttpPut("{id:guid}")]
    [Authorize(Roles = "SuperAdmin,Admin")]
    public async Task<ActionResult<DesignationResponse>> Update(Guid id, [FromBody] UpdateDesignationRequest request)
    {
        if (!await EnsureUserManagementPermissionAsync())
            return Forbid();

        try
        {
            var designation = await _designationService.UpdateDesignationAsync(id, request);
            return Ok(designation);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    /// <summary>
    /// Delete (soft-delete) a designation. Admin and SuperAdmin only.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "SuperAdmin,Admin")]
    public async Task<ActionResult> Delete(Guid id)
    {
        if (!await EnsureUserManagementPermissionAsync())
            return Forbid();

        try
        {
            await _designationService.DeleteDesignationAsync(id);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(ex.Message);
        }
    }

    private bool TryGetUserId(out Guid userId)
    {
        userId = Guid.Empty;
        var idClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return !string.IsNullOrWhiteSpace(idClaim) && Guid.TryParse(idClaim, out userId);
    }

    private bool TryGetRole(out UserRole role)
    {
        role = UserRole.GeneralUser;
        var roleClaim = User.FindFirstValue(ClaimTypes.Role);
        return !string.IsNullOrWhiteSpace(roleClaim) && Enum.TryParse(roleClaim, out role);
    }

    private async Task<bool> EnsureUserManagementPermissionAsync()
    {
        if (!TryGetUserId(out var userId) || !TryGetRole(out var role))
            return false;

        if (role == UserRole.SuperAdmin)
            return true;

        return role == UserRole.Admin
            && await _userService.HasPermissionAsync(userId, PermissionSlugs.DesignationManagement);
    }
}
