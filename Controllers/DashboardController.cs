using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using vision_backend.Application.Constants;
using vision_backend.Application.DTOs.Dashboard;
using vision_backend.Application.Interfaces;
using vision_backend.Domain.Enums;

namespace vision_backend.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class DashboardController : ControllerBase
{
    private readonly IDashboardService _dashboardService;
    private readonly IUserService _userService;

    public DashboardController(IDashboardService dashboardService, IUserService userService)
    {
        _dashboardService = dashboardService;
        _userService = userService;
    }

    [HttpGet("summary")]
    public async Task<ActionResult<DashboardSummaryDto>> GetSummary()
    {
        if (!TryGetUserId(out var userId) || !TryGetRole(out var role))
            return Unauthorized();

        var hasAnalyticsAccess = role == UserRole.SuperAdmin
            || await _userService.HasPermissionAsync(userId, PermissionSlugs.DashboardAnalytical);
        if (!hasAnalyticsAccess)
            return Forbid();

        var summary = await _dashboardService.GetDashboardSummaryAsync(userId, role);
        return Ok(summary);
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
}
