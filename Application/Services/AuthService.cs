using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using vision_backend.Application.DTOs.Auth;
using vision_backend.Application.DTOs.Users;
using vision_backend.Application.Constants;
using vision_backend.Application.Interfaces;
using vision_backend.Application.Options;
using vision_backend.Domain.Entities;
using vision_backend.Domain.Enums;
using vision_backend.Infrastructure.Data;
using vision_backend.Infrastructure.Repositories;

namespace vision_backend.Application.Services;

public class AuthService : IAuthService
{
    private readonly IUserRepository _userRepository;
    private readonly IRefreshTokenRepository _refreshTokenRepository;
    private readonly JwtOptions _jwtOptions;
    private readonly IStorageService _storage;
    private readonly ApplicationDbContext _context;

    public AuthService(
        IUserRepository userRepository,
        IRefreshTokenRepository refreshTokenRepository,
        IOptions<JwtOptions> jwtOptions,
        IStorageService storage,
        ApplicationDbContext context)
    {
        _userRepository = userRepository;
        _refreshTokenRepository = refreshTokenRepository;
        _jwtOptions = jwtOptions.Value;
        _storage = storage;
        _context = context;
    }

    public async Task<LoginResponse> LoginAsync(LoginRequest request)
    {
        // Normalise username: trim whitespace so accidental spaces don't block login.
        // Case-insensitive matching is handled inside the repository.
        var normalisedUsername = request.Username?.Trim() ?? string.Empty;
        var user = await _userRepository.GetByUsernameAsync(normalisedUsername);
        if (user == null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
        {
            throw new UnauthorizedAccessException("Invalid credentials.");
        }

        var accessToken = GenerateAccessToken(user, out var expiresAt);
        var refreshToken = await CreateRefreshTokenAsync(user.Id);

        return await BuildLoginResponseAsync(user, accessToken, refreshToken, expiresAt);
    }

    public async Task<LoginResponse> RefreshAccessTokenAsync(RefreshTokenRequest request)
    {
        var storedToken = await _refreshTokenRepository.GetByTokenAsync(request.RefreshToken);
        if (storedToken == null || storedToken.RevokedAt.HasValue || storedToken.ExpiresAt <= DateTime.Now)
        {
            throw new UnauthorizedAccessException("Refresh token is invalid.");
        }

        var user = await _userRepository.GetByIdAsync(storedToken.UserId);
        if (user == null)
        {
            throw new InvalidOperationException("User not found.");
        }

        await _refreshTokenRepository.RevokeAsync(storedToken.Id);
        var accessToken = GenerateAccessToken(user, out var expiresAt);
        var newRefreshToken = await CreateRefreshTokenAsync(user.Id);

        return await BuildLoginResponseAsync(user, accessToken, newRefreshToken, expiresAt);
    }

    public async Task ChangePasswordAsync(Guid userId, ChangePasswordRequest request)
    {
        var user = await _userRepository.GetByIdAsync(userId);
        if (user == null)
        {
            throw new InvalidOperationException("User not found.");
        }

        var currentPassword = request.CurrentPassword?.Trim();
        if (string.IsNullOrEmpty(currentPassword) || !BCrypt.Net.BCrypt.Verify(currentPassword, user.PasswordHash))
        {
            throw new InvalidOperationException("Current password is incorrect.");
        }

        var newPassword = request.NewPassword?.Trim();
        if (string.IsNullOrEmpty(newPassword) || newPassword.Length < 8)
        {
            throw new InvalidOperationException("New password must be at least 8 characters long.");
        }

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
        user.IsFirstLogin = false;

        await _userRepository.UpdateAsync(user);
    }

    public async Task<bool> VerifyPasswordAsync(Guid userId, string password)
    {
        var user = await _userRepository.GetByIdAsync(userId);
        if (user == null)
        {
            return false;
        }
        
        var trimmedPassword = password?.Trim();
        if (string.IsNullOrEmpty(trimmedPassword)) return false;

        return BCrypt.Net.BCrypt.Verify(trimmedPassword, user.PasswordHash);
    }

    private string GenerateAccessToken(User user, out DateTime expiresAt)
    {
        var effectiveRole = EffectiveRoleResolver.GetEffectiveRole(user);
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.Username),
            new Claim(ClaimTypes.Role, effectiveRole.ToString())
        };

        expiresAt = DateTime.Now.AddDays(_jwtOptions.ExpiryDays);
        var key = new SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(_jwtOptions.SecretKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _jwtOptions.Issuer,
            audience: _jwtOptions.Audience,
            claims: claims,
            expires: expiresAt,
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private async Task<string> CreateRefreshTokenAsync(Guid userId)
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        var refreshToken = new RefreshToken
        {
            Id = Guid.NewGuid(),
            Token = token,
            UserId = userId,
            CreatedAt = DateTime.Now,
            ExpiresAt = DateTime.Now.AddDays(_jwtOptions.ExpiryDays)
        };

        await _refreshTokenRepository.CreateAsync(refreshToken);

        return token;
    }

    private async Task<LoginResponse> BuildLoginResponseAsync(User user, string accessToken, string refreshToken, DateTime expiresAt)
    {
        var effectiveRole = EffectiveRoleResolver.GetEffectiveRole(user);
        var moduleAccess = await GetEffectivePermissionsForLoginAsync(user.Id, effectiveRole, user.AdminRoleId, user.SuperAdminId);

        return new LoginResponse
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            ExpiresAt = expiresAt,
            User = new UserResponse
            {
                Id = user.Id,
                Username = user.Username,
                Role = effectiveRole,
                Balance = user.Balance,
                FirstName = user.FirstName,
                LastName = user.LastName,
                DateOfBirth = user.DateOfBirth,
                MobileNumber = user.MobileNumber,
                IsFirstLogin = user.IsFirstLogin,
                ProfilePictureUrl = string.IsNullOrEmpty(user.ProfilePictureUrl)
                    ? null
                    : _storage.GetPresignedUrl(user.ProfilePictureUrl),
                AdminRoleId = user.AdminRoleId,
                AdminRoleName = user.AdminRole?.Name,
                SuperAdminId = user.SuperAdminId,
                ModuleAccess = moduleAccess,
            }
        };
    }

    private async Task<List<string>> GetEffectivePermissionsForLoginAsync(
        Guid userId,
        UserRole effectiveRole,
        Guid? adminRoleId,
        Guid? superAdminId)
    {
        if (effectiveRole == UserRole.SuperAdmin)
        {
            var all = await _context.Permissions.AsNoTracking().Select(p => p.Slug).ToListAsync();
            return all.Count > 0 ? all.Distinct(StringComparer.Ordinal).ToList() : PermissionSlugs.All.ToList();
        }

        if (effectiveRole == UserRole.Admin)
        {
            if (!adminRoleId.HasValue)
                return PermissionSlugs.BaselineForAllEmployees.ToList();

            var adminManaged = await _context.RolePermissions
                .AsNoTracking()
                .Where(rp => rp.RoleId == adminRoleId.Value)
                .Select(rp => rp.Permission.Slug)
                .Distinct()
                .ToListAsync();

            return PermissionSlugs.BaselineForAllEmployees
                .Concat(adminManaged)
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }

        if (effectiveRole == UserRole.Piller)
        {
            Guid? roleIdForPermissions = adminRoleId;

            if (!roleIdForPermissions.HasValue)
            {
                var normalizedPiller = EffectiveRoleResolver.PillerRoleName;

                roleIdForPermissions = await _context.AdminRoles
                    .AsNoTracking()
                    .Where(r => r.Name.ToLower() == normalizedPiller)
                    .OrderBy(r => r.CreatedBy == superAdminId ? 0 : 1)
                    .ThenBy(r => r.Name)
                    .Select(r => (Guid?)r.Id)
                    .FirstOrDefaultAsync();
            }

            if (!roleIdForPermissions.HasValue)
                return PermissionSlugs.BaselineForAllEmployees.ToList();

            var pillerManaged = await _context.RolePermissions
                .AsNoTracking()
                .Where(rp => rp.RoleId == roleIdForPermissions.Value)
                .Select(rp => rp.Permission.Slug)
                .Distinct()
                .ToListAsync();

            return PermissionSlugs.BaselineForAllEmployees
                .Concat(pillerManaged)
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }

        return PermissionSlugs.BaselineForAllEmployees.ToList();
    }
}
