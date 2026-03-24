namespace vision_backend.Application.DTOs.Vouchers;

public class BatchApproveResponse
{
    public List<VoucherResponse> Approved { get; set; } = new();
    public List<string> Errors { get; set; } = new();
}
