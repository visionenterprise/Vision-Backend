using vision_backend.Domain.Entities;
using vision_backend.Domain.Enums;

namespace vision_backend.Infrastructure.Repositories;

public interface IVoucherRepository
{
    Task<Voucher?> GetByIdAsync(Guid id);
    Task<List<Voucher>> GetByUserIdAsync(Guid userId);
    Task<List<Voucher>> GetForExportAsync(DateTime fromDate, DateTime toDate, Guid userId);
    Task<(List<Voucher> Vouchers, int TotalCount)> GetPagedAsync(
        int page,
        int pageSize,
        string? searchTerm,
        string? sortColumn,
        string? sortOrder,
        VoucherStatus? statusFilter,
        Guid? userIdFilter,
        bool approvalInbox,
        Guid? pillerApproverId,
        bool adminSharedApprovalAccess,
        Guid? superAdminApproverId);
    Task<int> GetNextSequenceNumberAsync(int year, int month);
    Task CreateAsync(Voucher voucher);
    Task UpdateAsync(Voucher voucher);
}
