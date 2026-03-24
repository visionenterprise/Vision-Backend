using vision_backend.Application.DTOs.Vouchers;
using vision_backend.Application.DTOs.Common;
using vision_backend.Domain.Enums;

namespace vision_backend.Application.Interfaces;

public interface IVoucherService
{
    Task<VoucherResponse> CreateVoucherAsync(Guid creatorId, CreateVoucherRequest request);
    Task<VoucherResponse> ApproveVoucherAsync(Guid voucherId, Guid approverId, UserRole approverRole);
    Task<VoucherResponse> RejectVoucherAsync(Guid voucherId, Guid rejecterId, UserRole role, string reason);
    Task<VoucherResponse> GetVoucherAsync(Guid voucherId);
    Task<List<VoucherResponse>> GetUserVouchersAsync(Guid userId);
    Task<PagedResult<VoucherResponse>> GetVouchersPagedAsync(VoucherSearchRequest request, Guid? currentUserId, UserRole role);
    Task<VoucherResponse> UploadReceiptsAsync(Guid voucherId, Guid userId, List<(Stream Stream, string FileName, string ContentType)> files);
    Task<(byte[] Content, string FileName)> ExportVouchersAsync(VoucherExportRequest request, Guid requesterId, UserRole requesterRole);
    Task<(byte[] Content, string FileName)> ExportVouchersPdfAsync(VoucherExportRequest request, Guid requesterId, UserRole requesterRole);
    Task<VoucherResponse> ReopenVoucherAsync(Guid voucherId, Guid actorId, UserRole role);
}
