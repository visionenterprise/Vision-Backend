using vision_backend.Application.DTOs.VoucherCategories;
using vision_backend.Application.Interfaces;
using vision_backend.Domain.Entities;
using vision_backend.Infrastructure.Repositories;

namespace vision_backend.Application.Services;

public class VoucherCategoryService : IVoucherCategoryService
{
    private readonly IVoucherCategoryRepository _repo;

    public VoucherCategoryService(IVoucherCategoryRepository repo)
    {
        _repo = repo;
    }

    public async Task<List<VoucherCategoryResponse>> GetAllAsync()
    {
        var categories = await _repo.GetAllAsync();
        return categories.Select(MapToResponse).ToList();
    }

    public async Task<List<VoucherCategoryResponse>> GetAllActiveAsync()
    {
        var categories = await _repo.GetAllActiveAsync();
        return categories.Select(MapToResponse).ToList();
    }

    public async Task<VoucherCategoryResponse> GetAsync(Guid id)
    {
        var category = await _repo.GetByIdAsync(id)
            ?? throw new InvalidOperationException("Category not found.");
        return MapToResponse(category);
    }

    public async Task<VoucherCategoryResponse> CreateAsync(CreateVoucherCategoryRequest request)
    {
        var existing = await _repo.GetByNameAsync(request.Name);
        if (existing != null)
        {
            if (existing.IsActive)
                throw new InvalidOperationException("A category with this name already exists.");

            // Reactivate soft-deleted category
            existing.IsActive = true;
            existing.SortOrder = request.SortOrder;
            existing.UpdatedAt = DateTime.Now;
            await _repo.UpdateAsync(existing);
            return MapToResponse(existing);
        }

        var now = DateTime.Now;
        var category = new VoucherCategoryConfig
        {
            Id = Guid.NewGuid(),
            Name = request.Name.Trim(),
            IsActive = true,
            SortOrder = request.SortOrder,
            CreatedAt = now,
            UpdatedAt = now,
        };

        await _repo.CreateAsync(category);
        return MapToResponse(category);
    }

    public async Task<VoucherCategoryResponse> UpdateAsync(Guid id, UpdateVoucherCategoryRequest request)
    {
        var category = await _repo.GetByIdAsync(id)
            ?? throw new InvalidOperationException("Category not found.");

        var existing = await _repo.GetByNameAsync(request.Name);
        if (existing != null && existing.Id != id)
            throw new InvalidOperationException("A category with this name already exists.");

        category.Name = request.Name.Trim();
        category.IsActive = request.IsActive;
        category.SortOrder = request.SortOrder;
        category.UpdatedAt = DateTime.Now;

        await _repo.UpdateAsync(category);
        return MapToResponse(category);
    }

    public async Task DeleteAsync(Guid id)
    {
        var category = await _repo.GetByIdAsync(id)
            ?? throw new InvalidOperationException("Category not found.");
        await _repo.DeleteAsync(category);
    }

    private static VoucherCategoryResponse MapToResponse(VoucherCategoryConfig c) =>
        new()
        {
            Id = c.Id,
            Name = c.Name,
            IsActive = c.IsActive,
            SortOrder = c.SortOrder,
            CreatedAt = c.CreatedAt,
            UpdatedAt = c.UpdatedAt,
        };
}
