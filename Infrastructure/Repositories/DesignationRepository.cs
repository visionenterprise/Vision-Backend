using Microsoft.EntityFrameworkCore;
using vision_backend.Domain.Entities;
using vision_backend.Infrastructure.Data;

namespace vision_backend.Infrastructure.Repositories;

public class DesignationRepository : IDesignationRepository
{
    private readonly ApplicationDbContext _context;

    public DesignationRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<List<Designation>> GetAllActiveAsync()
    {
        return await _context.Designations
            .Where(d => d.IsActive)
            .OrderBy(d => d.Name)
            .ToListAsync();
    }

    public async Task<Designation?> GetByIdAsync(Guid id)
    {
        return await _context.Designations.FindAsync(id);
    }

    public async Task<Designation?> GetByNameAsync(string name)
    {
        return await _context.Designations
            .FirstOrDefaultAsync(d => d.Name.ToLower() == name.ToLower());
    }

    public async Task CreateAsync(Designation designation)
    {
        _context.Designations.Add(designation);
        await _context.SaveChangesAsync();
    }

    public async Task UpdateAsync(Designation designation)
    {
        _context.Designations.Update(designation);
        await _context.SaveChangesAsync();
    }

    public async Task DeleteAsync(Designation designation)
    {
        // Soft delete
        designation.IsActive = false;
        designation.UpdatedAt = DateTime.Now;
        _context.Designations.Update(designation);
        await _context.SaveChangesAsync();
    }
}
