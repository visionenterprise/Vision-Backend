using vision_backend.Application.DTOs.Auth;

namespace vision_backend.Application.Interfaces;

public interface IAuthService
{
    Task<LoginResponse> LoginAsync(LoginRequest request);
    Task<LoginResponse> RefreshAccessTokenAsync(RefreshTokenRequest request);
    Task ChangePasswordAsync(Guid userId, ChangePasswordRequest request);
    Task<bool> VerifyPasswordAsync(Guid userId, string password);
}
