using vision_backend.Domain.Entities;

namespace vision_backend.Infrastructure.Repositories;

public interface IVoucherCategoryRepository
{
    Task<List<VoucherCategoryConfig>> GetAllAsync();
    Task<List<VoucherCategoryConfig>> GetAllActiveAsync();
    Task<VoucherCategoryConfig?> GetByIdAsync(Guid id);
    Task<VoucherCategoryConfig?> GetByNameAsync(string name);
    Task CreateAsync(VoucherCategoryConfig category);
    Task UpdateAsync(VoucherCategoryConfig category);
    Task DeleteAsync(VoucherCategoryConfig category);
}
