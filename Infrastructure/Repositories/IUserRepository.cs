using vision_backend.Domain.Entities;
using vision_backend.Domain.Enums;

namespace vision_backend.Infrastructure.Repositories;

public interface IUserRepository
{
    Task<User?> GetByIdAsync(Guid id);
    Task<User?> GetByUsernameAsync(string username);
    Task<List<User>> GetAllAsync();
    
    Task<(List<User> Users, int TotalCount)> GetPagedAsync(
        int page, 
        int pageSize, 
        string? searchTerm, 
        string? sortColumn, 
        string? sortOrder,
        UserRole? roleFilter,
        Guid? excludeUserId = null);

    Task<bool> ExistsAsync(string username);
    Task CreateAsync(User user);
    Task UpdateAsync(User user);
    Task DeleteAsync(Guid id);
}
