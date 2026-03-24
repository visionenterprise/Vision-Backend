using vision_backend.Domain.Entities;

namespace vision_backend.Infrastructure.Repositories;

public interface IDesignationRepository
{
    Task<List<Designation>> GetAllActiveAsync();
    Task<Designation?> GetByIdAsync(Guid id);
    Task<Designation?> GetByNameAsync(string name);
    Task CreateAsync(Designation designation);
    Task UpdateAsync(Designation designation);
    Task DeleteAsync(Designation designation);
}
