using vision_backend.Application.DTOs.Designations;

namespace vision_backend.Application.Interfaces;

public interface IDesignationService
{
    Task<List<DesignationResponse>> GetAllDesignationsAsync();
    Task<DesignationResponse> GetDesignationAsync(Guid id);
    Task<DesignationResponse> CreateDesignationAsync(CreateDesignationRequest request);
    Task<DesignationResponse> UpdateDesignationAsync(Guid id, UpdateDesignationRequest request);
    Task DeleteDesignationAsync(Guid id);
}
