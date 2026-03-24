using System.ComponentModel.DataAnnotations;

namespace vision_backend.Application.DTOs.Vouchers;

public class BatchApproveRequest
{
    [Required]
    public List<Guid> VoucherIds { get; set; } = new();
}
