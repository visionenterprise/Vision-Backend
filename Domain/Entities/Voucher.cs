using vision_backend.Domain.Enums;

namespace vision_backend.Domain.Entities;

public class Voucher
{
    public Guid Id { get; set; }
    public string VoucherNumber { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal Amount { get; set; }
    public DateTime VoucherDate { get; set; }
    /// <summary>Category name stored as free-text string (was enum int before migration).</summary>
    public string Category { get; set; } = string.Empty;
    public string SiteName { get; set; } = string.Empty;
    public VoucherStatus Status { get; set; }
    public LeaveApprovalLevel CurrentApprovalLevel { get; set; }
    public List<string>? ReceiptUrls { get; set; } = new();
    public Guid? TargetPillerId { get; set; }
    public User? TargetPiller { get; set; }
    public Guid? OwningSuperAdminId { get; set; }
    public User? OwningSuperAdmin { get; set; }

    // Creator
    public Guid CreatedById { get; set; }
    public User CreatedBy { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Admin approval
    public Guid? PillerApprovedById { get; set; }
    public User? PillerApprovedBy { get; set; }
    public DateTime? PillerApprovedAt { get; set; }

    // Admin approval
    public Guid? AdminApprovedById { get; set; }
    public User? AdminApprovedBy { get; set; }
    public DateTime? AdminApprovedAt { get; set; }

    // SuperAdmin approval
    public Guid? SuperAdminApprovedById { get; set; }
    public User? SuperAdminApprovedBy { get; set; }
    public DateTime? SuperAdminApprovedAt { get; set; }

    // Rejection
    public Guid? RejectedById { get; set; }
    public User? RejectedBy { get; set; }
    public DateTime? RejectedAt { get; set; }
    public string? RejectionReason { get; set; }
}
