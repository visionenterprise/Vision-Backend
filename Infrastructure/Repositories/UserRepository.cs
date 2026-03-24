using Microsoft.EntityFrameworkCore;
using vision_backend.Domain.Entities;
using vision_backend.Domain.Enums;
using vision_backend.Infrastructure.Data;

namespace vision_backend.Infrastructure.Repositories;

public class UserRepository : IUserRepository
{
    private readonly ApplicationDbContext _context;

    public UserRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<User?> GetByIdAsync(Guid id)
    {
        return await _context.Users
            .Include(u => u.Designation)
            .Include(u => u.AdminRole)
                .ThenInclude(r => r!.RolePermissions)
                    .ThenInclude(rp => rp.Permission)
            .Include(u => u.SuperAdmin)
            .AsNoTracking()
            .FirstOrDefaultAsync(user => user.Id == id);
    }

    public async Task<User?> GetByUsernameAsync(string username)
    {
        var normalised = username.Trim().ToLowerInvariant();
        return await _context.Users
            .Include(u => u.Designation)
            .Include(u => u.AdminRole)
                .ThenInclude(r => r!.RolePermissions)
                    .ThenInclude(rp => rp.Permission)
            .Include(u => u.SuperAdmin)
            .AsNoTracking()
            .FirstOrDefaultAsync(user => user.Username.ToLower() == normalised);
    }

    public async Task<List<User>> GetAllAsync()
    {
        return await _context.Users
            .Include(u => u.Designation)
            .Include(u => u.AdminRole)
            .Include(u => u.SuperAdmin)
            .AsNoTracking()
            .ToListAsync();
    }

    public async Task<bool> ExistsAsync(string username)
    {
        var normalised = username.Trim().ToLowerInvariant();
        return await _context.Users.AnyAsync(user => user.Username.ToLower() == normalised);
    }

    public async Task CreateAsync(User user)
    {
        _context.Users.Add(user);
        await _context.SaveChangesAsync();
    }

    public async Task UpdateAsync(User user)
    {
        var tracked = await _context.Users.FirstOrDefaultAsync(existing => existing.Id == user.Id);
        if (tracked == null)
        {
            _context.Users.Update(user);
        }
        else
        {
            _context.Entry(tracked).CurrentValues.SetValues(user);
        }
        await _context.SaveChangesAsync();
    }

    public async Task DeleteAsync(Guid id)
    {
        var user = await _context.Users.FindAsync(id);
        if (user != null)
        {
            _context.Users.Remove(user);
            await _context.SaveChangesAsync();
        }
    }

    public async Task<(List<User> Users, int TotalCount)> GetPagedAsync(
        int page, 
        int pageSize, 
        string? searchTerm, 
        string? sortColumn, 
        string? sortOrder,
        UserRole? roleFilter,
        Guid? excludeUserId)
    {
        var query = _context.Users
            .Include(u => u.Designation)
            .Include(u => u.AdminRole)
            .Include(u => u.SuperAdmin)
            .AsNoTracking()
            .AsQueryable();

        // Filtering
        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            searchTerm = searchTerm.ToLower();
            query = query.Where(u => 
                u.FirstName.ToLower().Contains(searchTerm) || 
                u.LastName.ToLower().Contains(searchTerm) || 
                u.Username.ToLower().Contains(searchTerm) ||
                u.MobileNumber.Contains(searchTerm));
        }

        if (roleFilter.HasValue)
        {
            if (roleFilter.Value == UserRole.Piller)
            {
                query = query.Where(u =>
                    u.Role == UserRole.Piller
                    || (u.Role == UserRole.Admin
                        && u.AdminRole != null
                        && u.AdminRole.Name.ToLower() == "piller"));
            }
            else
            {
                query = query.Where(u => u.Role == roleFilter.Value);
            }
        }

        if (excludeUserId.HasValue)
        {
            query = query.Where(u => u.Id != excludeUserId.Value);
        }

        var totalCount = await query.CountAsync();

        // Sorting
        query = sortColumn?.ToLower() switch
        {
            "username" => sortOrder == "desc" ? query.OrderByDescending(u => u.Username) : query.OrderBy(u => u.Username),
            "firstname" => sortOrder == "desc" ? query.OrderByDescending(u => u.FirstName) : query.OrderBy(u => u.FirstName),
            "lastname" => sortOrder == "desc" ? query.OrderByDescending(u => u.LastName) : query.OrderBy(u => u.LastName),
            "balance" => sortOrder == "desc" ? query.OrderByDescending(u => u.Balance) : query.OrderBy(u => u.Balance),
            "role" => sortOrder == "desc" ? query.OrderByDescending(u => u.Role) : query.OrderBy(u => u.Role),
            _ => query.OrderByDescending(u => u.Id) // Default sort
        };

        // Paging
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (items, totalCount);
    }
}
