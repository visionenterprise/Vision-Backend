using vision_backend.Application.DTOs.Dashboard;
using vision_backend.Domain.Enums;

namespace vision_backend.Application.Interfaces;

public interface IDashboardService
{
    Task<DashboardSummaryDto> GetDashboardSummaryAsync(Guid userId, UserRole role);
}
