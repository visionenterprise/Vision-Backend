using vision_backend.Application.DTOs.Sites;
using vision_backend.Application.Interfaces;
using vision_backend.Domain.Entities;
using vision_backend.Infrastructure.Repositories;

namespace vision_backend.Application.Services;

public class SiteService : ISiteService
{
    private readonly ISiteRepository _siteRepository;

    public SiteService(ISiteRepository siteRepository)
    {
        _siteRepository = siteRepository;
    }

    public async Task<List<SiteResponse>> GetAllSitesAsync()
    {
        var sites = await _siteRepository.GetAllActiveAsync();
        return sites.Select(MapToResponse).ToList();
    }

    public async Task<SiteResponse> GetSiteAsync(Guid id)
    {
        var site = await _siteRepository.GetByIdAsync(id)
            ?? throw new InvalidOperationException("Site not found.");

        return MapToResponse(site);
    }

    public async Task<SiteResponse> CreateSiteAsync(CreateSiteRequest request)
    {
        // Check for duplicate name
        var existing = await _siteRepository.GetByNameAsync(request.Name);
        if (existing != null)
        {
            if (existing.IsActive)
            {
                throw new InvalidOperationException("A site with this name already exists.");
            }
            else
            {
                // Reactivate soft-deleted site
                existing.IsActive = true;
                existing.UpdatedAt = DateTime.Now;
                await _siteRepository.UpdateAsync(existing);
                return MapToResponse(existing);
            }
        }

        var now = DateTime.Now;
        var site = new Site
        {
            Id = Guid.NewGuid(),
            Name = request.Name.Trim(),
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now,
        };

        await _siteRepository.CreateAsync(site);
        return MapToResponse(site);
    }

    public async Task<SiteResponse> UpdateSiteAsync(Guid id, UpdateSiteRequest request)
    {
        var site = await _siteRepository.GetByIdAsync(id)
            ?? throw new InvalidOperationException("Site not found.");

        // Check for duplicate name (exclude current site)
        var existing = await _siteRepository.GetByNameAsync(request.Name);
        if (existing != null && existing.Id != id)
            throw new InvalidOperationException("A site with this name already exists.");

        site.Name = request.Name.Trim();
        site.UpdatedAt = DateTime.Now;

        await _siteRepository.UpdateAsync(site);
        return MapToResponse(site);
    }

    public async Task DeleteSiteAsync(Guid id)
    {
        var site = await _siteRepository.GetByIdAsync(id)
            ?? throw new InvalidOperationException("Site not found.");

        await _siteRepository.DeleteAsync(site);
    }

    private static SiteResponse MapToResponse(Site site)
    {
        return new SiteResponse
        {
            Id = site.Id,
            Name = site.Name,
            IsActive = site.IsActive,
            CreatedAt = site.CreatedAt,
            UpdatedAt = site.UpdatedAt,
        };
    }
}
