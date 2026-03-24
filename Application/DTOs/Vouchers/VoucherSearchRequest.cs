using vision_backend.Domain.Enums;

namespace vision_backend.Application.DTOs.Vouchers;

public class VoucherSearchRequest
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
    public string? SearchTerm { get; set; }
    public string? SortColumn { get; set; }
    public string? SortOrder { get; set; }
    public VoucherStatus? StatusFilter { get; set; }
    public Guid? UserIdFilter { get; set; }
    public bool ApprovalInbox { get; set; } = false;
}
