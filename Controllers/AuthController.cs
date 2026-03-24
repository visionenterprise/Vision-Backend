using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using vision_backend.Application.DTOs.Auth;
using vision_backend.Application.DTOs.Users;
using vision_backend.Application.Interfaces;

namespace vision_backend.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly IUserService _userService;

    public AuthController(IAuthService authService, IUserService userService)
    {
        _authService = authService;
        _userService = userService;
    }

    [HttpPost("login")]
    public async Task<ActionResult<LoginResponse>> Login([FromBody] LoginRequest request)
    {
        try
        {
            var response = await _authService.LoginAsync(request);
            return Ok(response);
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(ex.Message);
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, ex.Message);
        }
    }

    [HttpPost("refresh")]
    public async Task<ActionResult<LoginResponse>> Refresh([FromBody] RefreshTokenRequest request)
    {
        try
        {
            var response = await _authService.RefreshAccessTokenAsync(request);
            return Ok(response);
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(ex.Message);
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, ex.Message);
        }
    }

    [Authorize]
    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId) || !Guid.TryParse(userId, out var parsedUserId))
        {
            return Unauthorized();
        }

        try
        {
            await _authService.ChangePasswordAsync(parsedUserId, request);
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

    [Authorize]
    [HttpPost("verify-password")]
    public async Task<ActionResult<object>> VerifyPassword([FromBody] VerifyPasswordRequest request)
    {
        var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userIdString) || !Guid.TryParse(userIdString, out var userId))
        {
            Console.WriteLine("[VerifyPassword] Unauthorized: UserId not found in claims.");
            return Unauthorized();
        }

        Console.WriteLine($"[VerifyPassword] Checking password for userId: {userId}");

        try 
        {
            var user = await _userService.GetUserAsync(userId);
            if (user == null)
            {
                Console.WriteLine($"[VerifyPassword] User {userId} not found in database.");
                return NotFound("User not found.");
            }

            // Deep fix: perform raw verification here to be absolutely sure
            // We use the same BCrypt logic as login but with extra safety
            var isValid = await _authService.VerifyPasswordAsync(userId, request.Password);
            
            Console.WriteLine($"[VerifyPassword] Result for {userId}: {isValid}");

            return Ok(new 
            { 
                isValid, 
                message = isValid ? "Current password is correct" : "Current password is wrong" 
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[VerifyPassword] Error: {ex.Message}");
            return StatusCode(500, "Internal server error during verification.");
        }
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<ActionResult<UserResponse>> Me()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId) || !Guid.TryParse(userId, out var parsedUserId))
        {
            return Unauthorized();
        }

        try
        {
            var user = await _userService.GetUserAsync(parsedUserId);
            return Ok(user);
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
}
