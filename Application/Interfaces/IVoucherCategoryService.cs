using vision_backend.Application.DTOs.VoucherCategories;

namespace vision_backend.Application.Interfaces;

public interface IVoucherCategoryService
{
    Task<List<VoucherCategoryResponse>> GetAllAsync();
    Task<List<VoucherCategoryResponse>> GetAllActiveAsync();
    Task<VoucherCategoryResponse> GetAsync(Guid id);
    Task<VoucherCategoryResponse> CreateAsync(CreateVoucherCategoryRequest request);
    Task<VoucherCategoryResponse> UpdateAsync(Guid id, UpdateVoucherCategoryRequest request);
    Task DeleteAsync(Guid id);
}
