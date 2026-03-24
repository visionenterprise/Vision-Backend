using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using vision_backend.Application.Constants;
using vision_backend.Application.DTOs.Sites;
using vision_backend.Application.Interfaces;
using vision_backend.Domain.Enums;

namespace vision_backend.Controllers;

[ApiController]
[Route("api/sites")]
[Authorize]
public class SitesController : ControllerBase
{
    private readonly ISiteService _siteService;
    private readonly IUserService _userService;

    public SitesController(ISiteService siteService, IUserService userService)
    {
        _siteService = siteService;
        _userService = userService;
    }

    /// <summary>
    /// Get all active sites. Available to all authenticated users (for dropdown in voucher form).
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<List<SiteResponse>>> GetAll()
    {
        var sites = await _siteService.GetAllSitesAsync();
        return Ok(sites);
    }

    /// <summary>
    /// Get a single site by ID.
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<SiteResponse>> GetById(Guid id)
    {
        try
        {
            var site = await _siteService.GetSiteAsync(id);
            return Ok(site);
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(ex.Message);
        }
    }

    /// <summary>
    /// Create a new site. Admin and SuperAdmin only.
    /// </summary>
    [HttpPost]
    [Authorize(Roles = "SuperAdmin,Admin")]
    public async Task<ActionResult<SiteResponse>> Create([FromBody] CreateSiteRequest request)
    {
        if (!await EnsureUserManagementPermissionAsync())
            return Forbid();

        try
        {
            var site = await _siteService.CreateSiteAsync(request);
            return Ok(site);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    /// <summary>
    /// Update an existing site. Admin and SuperAdmin only.
    /// </summary>
    [HttpPut("{id:guid}")]
    [Authorize(Roles = "SuperAdmin,Admin")]
    public async Task<ActionResult<SiteResponse>> Update(Guid id, [FromBody] UpdateSiteRequest request)
    {
        if (!await EnsureUserManagementPermissionAsync())
            return Forbid();

        try
        {
            var site = await _siteService.UpdateSiteAsync(id, request);
            return Ok(site);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    /// <summary>
    /// Delete (soft-delete) a site. Admin and SuperAdmin only.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "SuperAdmin,Admin")]
    public async Task<ActionResult> Delete(Guid id)
    {
        if (!await EnsureUserManagementPermissionAsync())
            return Forbid();

        try
        {
            await _siteService.DeleteSiteAsync(id);
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
            && await _userService.HasPermissionAsync(userId, PermissionSlugs.SiteManagement);
    }
}
