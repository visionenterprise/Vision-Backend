using Microsoft.EntityFrameworkCore;
using vision_backend.Domain.Entities;
using vision_backend.Infrastructure.Data;

namespace vision_backend.Infrastructure.Repositories;

public class RefreshTokenRepository : IRefreshTokenRepository
{
    private readonly ApplicationDbContext _context;

    public RefreshTokenRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task CreateAsync(RefreshToken refreshToken)
    {
        _context.RefreshTokens.Add(refreshToken);
        await _context.SaveChangesAsync();
    }

    public async Task<RefreshToken?> GetByTokenAsync(string token)
    {
        return await _context.RefreshTokens.FirstOrDefaultAsync(refresh => refresh.Token == token);
    }

    public async Task RevokeAsync(Guid tokenId)
    {
        var refreshToken = await _context.RefreshTokens.FirstOrDefaultAsync(refresh => refresh.Id == tokenId);
        if (refreshToken == null)
        {
            return;
        }

        refreshToken.RevokedAt = DateTime.Now;
        await _context.SaveChangesAsync();
    }

    public async Task DeleteExpiredAsync(DateTime nowUtc)
    {
        var expiredTokens = await _context.RefreshTokens
            .Where(refresh => refresh.ExpiresAt <= nowUtc)
            .ToListAsync();

        if (expiredTokens.Count == 0)
        {
            return;
        }

        _context.RefreshTokens.RemoveRange(expiredTokens);
        await _context.SaveChangesAsync();
    }
}
