using vision_backend.Domain.Entities;

namespace vision_backend.Infrastructure.Repositories;

public interface ISiteRepository
{
    Task<List<Site>> GetAllActiveAsync();
    Task<Site?> GetByIdAsync(Guid id);
    Task<Site?> GetByNameAsync(string name);
    Task CreateAsync(Site site);
    Task UpdateAsync(Site site);
    Task DeleteAsync(Site site);
}
