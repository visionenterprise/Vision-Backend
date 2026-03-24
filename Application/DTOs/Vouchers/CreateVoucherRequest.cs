using System.ComponentModel.DataAnnotations;

namespace vision_backend.Application.DTOs.Vouchers;

public class CreateVoucherRequest
{
    [Required]
    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string? Description { get; set; }

    [Required]
    [Range(0.01, double.MaxValue, ErrorMessage = "Amount must be greater than 0")]
    public decimal Amount { get; set; }

    [Required]
    [MaxLength(100)]
    public string Category { get; set; } = string.Empty;

    [Required]
    public DateTime VoucherDate { get; set; }

    [Required]
    [MaxLength(200)]
    public string SiteName { get; set; } = string.Empty;

    public Guid? TargetPillerId { get; set; }
}
