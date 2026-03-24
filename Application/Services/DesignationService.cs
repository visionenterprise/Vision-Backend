using vision_backend.Application.DTOs.Designations;
using vision_backend.Application.Interfaces;
using vision_backend.Domain.Entities;
using vision_backend.Infrastructure.Repositories;

namespace vision_backend.Application.Services;

public class DesignationService : IDesignationService
{
    private readonly IDesignationRepository _designationRepository;

    public DesignationService(IDesignationRepository designationRepository)
    {
        _designationRepository = designationRepository;
    }

    public async Task<List<DesignationResponse>> GetAllDesignationsAsync()
    {
        var designations = await _designationRepository.GetAllActiveAsync();
        return designations.Select(MapToResponse).ToList();
    }

    public async Task<DesignationResponse> GetDesignationAsync(Guid id)
    {
        var designation = await _designationRepository.GetByIdAsync(id)
            ?? throw new InvalidOperationException("Designation not found.");

        return MapToResponse(designation);
    }

    public async Task<DesignationResponse> CreateDesignationAsync(CreateDesignationRequest request)
    {
        // Check for duplicate name
        var existing = await _designationRepository.GetByNameAsync(request.Name);
        if (existing != null)
        {
            if (existing.IsActive)
            {
                throw new InvalidOperationException("A designation with this name already exists.");
            }
            else
            {
                // Reactivate soft-deleted designation
                existing.IsActive = true;
                existing.UpdatedAt = DateTime.Now;
                await _designationRepository.UpdateAsync(existing);
                return MapToResponse(existing);
            }
        }

        var now = DateTime.Now;
        var designation = new Designation
        {
            Id = Guid.NewGuid(),
            Name = request.Name.Trim(),
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now,
        };

        await _designationRepository.CreateAsync(designation);
        return MapToResponse(designation);
    }

    public async Task<DesignationResponse> UpdateDesignationAsync(Guid id, UpdateDesignationRequest request)
    {
        var designation = await _designationRepository.GetByIdAsync(id)
            ?? throw new InvalidOperationException("Designation not found.");

        // Check for duplicate name (exclude current designation)
        var existing = await _designationRepository.GetByNameAsync(request.Name);
        if (existing != null && existing.Id != id)
            throw new InvalidOperationException("A designation with this name already exists.");

        designation.Name = request.Name.Trim();
        designation.UpdatedAt = DateTime.Now;

        await _designationRepository.UpdateAsync(designation);
        return MapToResponse(designation);
    }

    public async Task DeleteDesignationAsync(Guid id)
    {
        var designation = await _designationRepository.GetByIdAsync(id)
            ?? throw new InvalidOperationException("Designation not found.");

        await _designationRepository.DeleteAsync(designation);
    }

    private static DesignationResponse MapToResponse(Designation designation)
    {
        return new DesignationResponse
        {
            Id = designation.Id,
            Name = designation.Name,
            IsActive = designation.IsActive,
            CreatedAt = designation.CreatedAt,
            UpdatedAt = designation.UpdatedAt,
        };
    }
}
