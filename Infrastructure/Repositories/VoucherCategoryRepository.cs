using Microsoft.EntityFrameworkCore;
using vision_backend.Domain.Entities;
using vision_backend.Infrastructure.Data;

namespace vision_backend.Infrastructure.Repositories;

public class VoucherCategoryRepository : IVoucherCategoryRepository
{
    private readonly ApplicationDbContext _context;

    public VoucherCategoryRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<List<VoucherCategoryConfig>> GetAllAsync()
    {
        return await _context.VoucherCategories
            .OrderBy(c => c.SortOrder)
            .ThenBy(c => c.Name)
            .ToListAsync();
    }

    public async Task<List<VoucherCategoryConfig>> GetAllActiveAsync()
    {
        return await _context.VoucherCategories
            .Where(c => c.IsActive)
            .OrderBy(c => c.SortOrder)
            .ThenBy(c => c.Name)
            .ToListAsync();
    }

    public async Task<VoucherCategoryConfig?> GetByIdAsync(Guid id)
    {
        return await _context.VoucherCategories.FindAsync(id);
    }

    public async Task<VoucherCategoryConfig?> GetByNameAsync(string name)
    {
        return await _context.VoucherCategories
            .FirstOrDefaultAsync(c => c.Name.ToLower() == name.ToLower());
    }

    public async Task CreateAsync(VoucherCategoryConfig category)
    {
        _context.VoucherCategories.Add(category);
        await _context.SaveChangesAsync();
    }

    public async Task UpdateAsync(VoucherCategoryConfig category)
    {
        _context.VoucherCategories.Update(category);
        await _context.SaveChangesAsync();
    }

    public async Task DeleteAsync(VoucherCategoryConfig category)
    {
        // Soft delete
        category.IsActive = false;
        category.UpdatedAt = DateTime.Now;
        _context.VoucherCategories.Update(category);
        await _context.SaveChangesAsync();
    }
}
