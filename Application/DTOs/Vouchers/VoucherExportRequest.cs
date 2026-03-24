namespace vision_backend.Application.DTOs.Vouchers;

public class VoucherExportRequest
{
    public DateTime FromDate { get; set; }
    public DateTime ToDate { get; set; }
    public Guid? UserId { get; set; }
}
