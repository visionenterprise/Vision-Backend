using System.ComponentModel.DataAnnotations;

namespace vision_backend.Application.DTOs.Vouchers;

public class RejectVoucherRequest
{
    [Required]
    [MaxLength(500)]
    public string Reason { get; set; } = string.Empty;
}
