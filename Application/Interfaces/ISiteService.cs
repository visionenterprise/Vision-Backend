using vision_backend.Application.DTOs.Sites;

namespace vision_backend.Application.Interfaces;

public interface ISiteService
{
    Task<List<SiteResponse>> GetAllSitesAsync();
    Task<SiteResponse> GetSiteAsync(Guid id);
    Task<SiteResponse> CreateSiteAsync(CreateSiteRequest request);
    Task<SiteResponse> UpdateSiteAsync(Guid id, UpdateSiteRequest request);
    Task DeleteSiteAsync(Guid id);
}
