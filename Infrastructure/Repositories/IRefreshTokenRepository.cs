using vision_backend.Domain.Entities;

namespace vision_backend.Infrastructure.Repositories;

public interface IRefreshTokenRepository
{
    Task CreateAsync(RefreshToken refreshToken);
    Task<RefreshToken?> GetByTokenAsync(string token);
    Task RevokeAsync(Guid tokenId);
    Task DeleteExpiredAsync(DateTime nowUtc);
}
