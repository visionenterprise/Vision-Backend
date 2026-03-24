using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using vision_backend.Application.Constants;
using vision_backend.Application.DTOs.Leaves;
using vision_backend.Application.Interfaces;
using vision_backend.Domain.Enums;

namespace vision_backend.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class LeaveController : ControllerBase
{
    private readonly ILeaveService _leaveService;
    private readonly IUserService _userService;

    public LeaveController(ILeaveService leaveService, IUserService userService)
    {
        _leaveService = leaveService;
        _userService = userService;
    }

    [HttpPost("apply")]
    public async Task<IActionResult> ApplyForLeave([FromBody] ApplyLeaveRequest request)
    {
        try
        {
            var userId = GetCurrentUserId();
            var result = await _leaveService.ApplyForLeaveAsync(userId, request);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { Message = ex.Message });
        }
    }

    [HttpPost("{id}/approve")]
    public async Task<IActionResult> ApproveLeave(Guid id)
    {
        try
        {
            var userId = GetCurrentUserId();
            var userRole = GetCurrentUserRole();
            var result = await _leaveService.ApproveLeaveAsync(id, userId, userRole);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { Message = ex.Message });
        }
    }

    [HttpPost("{id}/reject")]
    public async Task<IActionResult> RejectLeave(Guid id, [FromBody] RejectLeaveRequest request)
    {
        try
        {
            var userId = GetCurrentUserId();
            var userRole = GetCurrentUserRole();
            var result = await _leaveService.RejectLeaveAsync(id, userId, userRole, request.Reason);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { Message = ex.Message });
        }
    }

    [HttpGet("my")]
    public async Task<IActionResult> GetMyLeaves()
    {
        var userId = GetCurrentUserId();
        var result = await _leaveService.GetMyLeavesAsync(userId);
        return Ok(result);
    }

    [HttpGet("pending-approvals")]
    [Authorize(Roles = "Piller,Admin,SuperAdmin")]
    public async Task<IActionResult> GetPendingApprovals()
    {
        try
        {
            var userId = GetCurrentUserId();
            var userRole = GetCurrentUserRole();

            if (userRole != UserRole.SuperAdmin && !await _userService.HasPermissionAsync(userId, PermissionSlugs.LeaveApprovals))
                return Forbid();

            var result = await _leaveService.GetPendingApprovalsAsync(userId, userRole);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { Message = ex.Message });
        }
    }

    [HttpGet("upcoming")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> GetUpcomingLeaves()
    {
        var userId = GetCurrentUserId();
        var userRole = GetCurrentUserRole();
        if (userRole == UserRole.Admin && !await _userService.HasPermissionAsync(userId, PermissionSlugs.UpcomingLeaves))
            return Forbid();

        var result = await _leaveService.GetUpcomingLeavesAsync();
        return Ok(result);
    }

    [HttpGet("leave-types")]
    public async Task<IActionResult> GetLeaveTypes([FromQuery] bool includeInactive = false)
    {
        var result = await _leaveService.GetLeaveTypesAsync(includeInactive);
        return Ok(result);
    }

    [HttpGet("leave-types/my")]
    public async Task<IActionResult> GetMyLeaveTypes()
    {
        var userId = GetCurrentUserId();
        var result = await _leaveService.GetMyAssignableLeaveTypesAsync(userId);
        return Ok(result);
    }

    [HttpGet("balances/my")]
    public async Task<IActionResult> GetMyLeaveBalances([FromQuery] int? year = null)
    {
        var userId = GetCurrentUserId();
        var result = await _leaveService.GetMyLeaveBalancesAsync(userId, year);
        return Ok(result);
    }

    [HttpPost("leave-types")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> CreateLeaveType([FromBody] CreateLeaveTypeRequest request)
    {
        try
        {
            var actorId = GetCurrentUserId();
            var result = await _leaveService.CreateLeaveTypeAsync(actorId, request);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { Message = ex.Message });
        }
    }

    [HttpPut("leave-types/{leaveTypeId:guid}")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> UpdateLeaveType(Guid leaveTypeId, [FromBody] UpdateLeaveTypeRequest request)
    {
        try
        {
            var actorId = GetCurrentUserId();
            var result = await _leaveService.UpdateLeaveTypeAsync(actorId, leaveTypeId, request);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { Message = ex.Message });
        }
    }

    [HttpDelete("leave-types/{leaveTypeId:guid}")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> DeleteLeaveType(Guid leaveTypeId)
    {
        try
        {
            var actorId = GetCurrentUserId();
            await _leaveService.DeleteLeaveTypeAsync(actorId, leaveTypeId);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { Message = ex.Message });
        }
    }

    [HttpGet("leave-types/{leaveTypeId:guid}/assignments")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> GetLeaveTypeAssignments(Guid leaveTypeId)
    {
        try
        {
            var result = await _leaveService.GetLeaveTypeAssignmentsAsync(leaveTypeId);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { Message = ex.Message });
        }
    }

    [HttpPost("leave-types/{leaveTypeId:guid}/assignments")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> AssignLeaveType(Guid leaveTypeId, [FromBody] AssignLeaveTypeRequest request)
    {
        try
        {
            var actorId = GetCurrentUserId();
            var result = await _leaveService.AssignLeaveTypeAsync(actorId, leaveTypeId, request);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { Message = ex.Message });
        }
    }

    [HttpPost("leave-types/{leaveTypeId:guid}/assign-batch")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> AssignLeaveTypeBatch(Guid leaveTypeId, [FromBody] AssignLeaveTypeBatchRequest request)
    {
        try
        {
            var actorId = GetCurrentUserId();
            var result = await _leaveService.AssignLeaveTypeBatchAsync(actorId, leaveTypeId, request);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { Message = ex.Message });
        }
    }

    [HttpPost("leave-types/{leaveTypeId:guid}/assign-all")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> AssignLeaveTypeToAll(Guid leaveTypeId, [FromBody] AssignLeaveTypeToAllRequest request)
    {
        try
        {
            var actorId = GetCurrentUserId();
            var result = await _leaveService.AssignLeaveTypeToAllUsersAsync(actorId, leaveTypeId, request);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { Message = ex.Message });
        }
    }

    [HttpPut("leave-type-assignments/{assignmentId:guid}")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> UpdateLeaveTypeAssignment(Guid assignmentId, [FromBody] AssignLeaveTypeRequest request)
    {
        try
        {
            var actorId = GetCurrentUserId();
            var result = await _leaveService.UpdateLeaveTypeAssignmentAsync(actorId, assignmentId, request);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { Message = ex.Message });
        }
    }

    [HttpDelete("leave-type-assignments/{assignmentId:guid}")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> DeleteLeaveTypeAssignment(Guid assignmentId)
    {
        try
        {
            var actorId = GetCurrentUserId();
            await _leaveService.DeleteLeaveTypeAssignmentAsync(actorId, assignmentId);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { Message = ex.Message });
        }
    }

    [HttpGet("holidays")]
    public async Task<IActionResult> GetPublicHolidays([FromQuery] DateOnly? fromDate = null, [FromQuery] DateOnly? toDate = null, [FromQuery] bool includeInactive = false)
    {
        var result = await _leaveService.GetPublicHolidaysAsync(fromDate, toDate, includeInactive);
        return Ok(result);
    }

    [HttpPost("holidays")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> CreatePublicHoliday([FromBody] CreatePublicHolidayRequest request)
    {
        try
        {
            var actorId = GetCurrentUserId();
            var result = await _leaveService.CreatePublicHolidayAsync(actorId, request);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { Message = ex.Message });
        }
    }

    [HttpPut("holidays/{holidayId:guid}")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> UpdatePublicHoliday(Guid holidayId, [FromBody] UpdatePublicHolidayRequest request)
    {
        try
        {
            var actorId = GetCurrentUserId();
            var result = await _leaveService.UpdatePublicHolidayAsync(actorId, holidayId, request);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { Message = ex.Message });
        }
    }

    [HttpDelete("holidays/{holidayId:guid}")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> DeletePublicHoliday(Guid holidayId)
    {
        try
        {
            var actorId = GetCurrentUserId();
            await _leaveService.DeletePublicHolidayAsync(actorId, holidayId);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { Message = ex.Message });
        }
    }
    
    // Helpers
    private Guid GetCurrentUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier);
        if (claim == null || !Guid.TryParse(claim.Value, out var userId))
            throw new UnauthorizedAccessException("Invalid token.");
        return userId;
    }

    private UserRole GetCurrentUserRole()
    {
        var roleClaim = User.FindFirst(ClaimTypes.Role)?.Value;
        if (Enum.TryParse<UserRole>(roleClaim, out var role))
            return role;
        throw new UnauthorizedAccessException("Invalid role in token.");
    }
}
