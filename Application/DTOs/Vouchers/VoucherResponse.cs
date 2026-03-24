using vision_backend.Domain.Enums;

namespace vision_backend.Application.DTOs.Vouchers;

public class VoucherResponse
{
    public Guid Id { get; set; }
    public string VoucherNumber { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal Amount { get; set; }
    public DateTime VoucherDate { get; set; }
    public string Category { get; set; } = string.Empty;
    public string CategoryName { get; set; } = string.Empty;
    public string SiteName { get; set; } = string.Empty;
    public VoucherStatus Status { get; set; }
    public string StatusName { get; set; } = string.Empty;
    public LeaveApprovalLevel CurrentApprovalLevel { get; set; }
    public List<string> ReceiptUrls { get; set; } = new();
    public Guid? TargetPillerId { get; set; }
    public string? TargetPillerName { get; set; }
    public string? AssignedAdminName { get; set; }
    public Guid? OwningSuperAdminId { get; set; }
    public string? OwningSuperAdminName { get; set; }

    // Creator info
    public Guid CreatedById { get; set; }
    public string CreatedByName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Admin approval info
    public Guid? PillerApprovedById { get; set; }
    public string? PillerApprovedByName { get; set; }
    public DateTime? PillerApprovedAt { get; set; }

    // Admin approval info
    public Guid? AdminApprovedById { get; set; }
    public string? AdminApprovedByName { get; set; }
    public DateTime? AdminApprovedAt { get; set; }

    // SuperAdmin approval info
    public Guid? SuperAdminApprovedById { get; set; }
    public string? SuperAdminApprovedByName { get; set; }
    public DateTime? SuperAdminApprovedAt { get; set; }

    // Rejection info
    public Guid? RejectedById { get; set; }
    public string? RejectedByName { get; set; }
    public DateTime? RejectedAt { get; set; }
    public string? RejectionReason { get; set; }
    public DateTime? ReopenDeadlineAt { get; set; }
}
