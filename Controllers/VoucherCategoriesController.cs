using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using vision_backend.Application.Constants;
using vision_backend.Application.DTOs.VoucherCategories;
using vision_backend.Application.Interfaces;
using vision_backend.Domain.Enums;

namespace vision_backend.Controllers;

[ApiController]
[Route("api/voucher-categories")]
[Authorize]
public class VoucherCategoriesController : ControllerBase
{
    private readonly IVoucherCategoryService _service;
    private readonly IUserService _userService;

    public VoucherCategoriesController(IVoucherCategoryService service, IUserService userService)
    {
        _service = service;
        _userService = userService;
    }

    /// <summary>
    /// Get all active categories (for dropdown use). Available to all authenticated users.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<List<VoucherCategoryResponse>>> GetActive()
    {
        var categories = await _service.GetAllActiveAsync();
        return Ok(categories);
    }

    /// <summary>
    /// Get all categories including inactive (admin view).
    /// </summary>
    [HttpGet("all")]
    [Authorize(Roles = "SuperAdmin,Admin,Piller")]
    public async Task<ActionResult<List<VoucherCategoryResponse>>> GetAll()
    {
        if (!await EnsureVoucherManagementPermissionAsync())
            return Forbid();

        var categories = await _service.GetAllAsync();
        return Ok(categories);
    }

    /// <summary>
    /// Create a new voucher category. Admin and SuperAdmin only.
    /// </summary>
    [HttpPost]
    [Authorize(Roles = "SuperAdmin,Admin,Piller")]
    public async Task<ActionResult<VoucherCategoryResponse>> Create([FromBody] CreateVoucherCategoryRequest request)
    {
        if (!await EnsureVoucherManagementPermissionAsync())
            return Forbid();

        try
        {
            var result = await _service.CreateAsync(request);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    /// <summary>
    /// Update an existing category. Admin and SuperAdmin only.
    /// </summary>
    [HttpPut("{id:guid}")]
    [Authorize(Roles = "SuperAdmin,Admin,Piller")]
    public async Task<ActionResult<VoucherCategoryResponse>> Update(Guid id, [FromBody] UpdateVoucherCategoryRequest request)
    {
        if (!await EnsureVoucherManagementPermissionAsync())
            return Forbid();

        try
        {
            var result = await _service.UpdateAsync(id, request);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    /// <summary>
    /// Delete (soft-delete) a category. Admin and SuperAdmin only.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "SuperAdmin,Admin,Piller")]
    public async Task<ActionResult> Delete(Guid id)
    {
        if (!await EnsureVoucherManagementPermissionAsync())
            return Forbid();

        try
        {
            await _service.DeleteAsync(id);
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

    private async Task<bool> EnsureVoucherManagementPermissionAsync()
    {
        if (!TryGetUserId(out var userId) || !TryGetRole(out var role))
            return false;

        if (role == UserRole.SuperAdmin)
            return true;

        if (role != UserRole.Admin && role != UserRole.Piller)
            return false;

        return await _userService.HasPermissionAsync(userId, PermissionSlugs.VoucherCategories);
    }
}
