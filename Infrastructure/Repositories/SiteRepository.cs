using Microsoft.EntityFrameworkCore;
using vision_backend.Domain.Entities;
using vision_backend.Infrastructure.Data;

namespace vision_backend.Infrastructure.Repositories;

public class SiteRepository : ISiteRepository
{
    private readonly ApplicationDbContext _context;

    public SiteRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<List<Site>> GetAllActiveAsync()
    {
        return await _context.Sites
            .Where(s => s.IsActive)
            .OrderBy(s => s.Name)
            .ToListAsync();
    }

    public async Task<Site?> GetByIdAsync(Guid id)
    {
        return await _context.Sites.FindAsync(id);
    }

    public async Task<Site?> GetByNameAsync(string name)
    {
        return await _context.Sites
            .FirstOrDefaultAsync(s => s.Name.ToLower() == name.ToLower());
    }

    public async Task CreateAsync(Site site)
    {
        _context.Sites.Add(site);
        await _context.SaveChangesAsync();
    }

    public async Task UpdateAsync(Site site)
    {
        _context.Sites.Update(site);
        await _context.SaveChangesAsync();
    }

    public async Task DeleteAsync(Site site)
    {
        // Soft delete
        site.IsActive = false;
        site.UpdatedAt = DateTime.Now;
        _context.Sites.Update(site);
        await _context.SaveChangesAsync();
    }
}
