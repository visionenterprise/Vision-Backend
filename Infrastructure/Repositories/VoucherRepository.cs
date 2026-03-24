using Microsoft.EntityFrameworkCore;
using vision_backend.Domain.Entities;
using vision_backend.Domain.Enums;
using vision_backend.Infrastructure.Data;

namespace vision_backend.Infrastructure.Repositories;

public class VoucherRepository : IVoucherRepository
{
    private readonly ApplicationDbContext _context;

    public VoucherRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Voucher?> GetByIdAsync(Guid id)
    {
        return await _context.Vouchers
            .Include(v => v.CreatedBy)
            .Include(v => v.TargetPiller)
            .Include(v => v.OwningSuperAdmin)
            .Include(v => v.PillerApprovedBy)
            .Include(v => v.AdminApprovedBy)
            .Include(v => v.SuperAdminApprovedBy)
            .Include(v => v.RejectedBy)
            .FirstOrDefaultAsync(v => v.Id == id);
    }

    public async Task<List<Voucher>> GetByUserIdAsync(Guid userId)
    {
        return await _context.Vouchers
            .Include(v => v.CreatedBy)
            .Include(v => v.TargetPiller)
            .Include(v => v.OwningSuperAdmin)
            .Include(v => v.PillerApprovedBy)
            .Where(v => v.CreatedById == userId)
            .OrderByDescending(v => v.CreatedAt)
            .ToListAsync();
    }

    public async Task<List<Voucher>> GetForExportAsync(DateTime fromDate, DateTime toDate, Guid userId)
    {
        var approvedStatuses = new[] { VoucherStatus.ApprovedAdmin, VoucherStatus.PendingSuperAdmin, VoucherStatus.Approved };

        return await _context.Vouchers
            .Include(v => v.CreatedBy)
            .Include(v => v.TargetPiller)
            .Include(v => v.OwningSuperAdmin)
            .Include(v => v.PillerApprovedBy)
            .Include(v => v.AdminApprovedBy)
            .Include(v => v.SuperAdminApprovedBy)
            .Where(v =>
                v.CreatedById == userId &&
                approvedStatuses.Contains(v.Status) &&
                v.VoucherDate >= fromDate &&
                v.VoucherDate < toDate.AddDays(1))
            .OrderBy(v => v.VoucherDate)
            .ThenBy(v => v.CreatedAt)
            .ToListAsync();
    }

    public async Task<(List<Voucher> Vouchers, int TotalCount)> GetPagedAsync(
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
        Guid? superAdminApproverId)
    {
        var query = _context.Vouchers
            .Include(v => v.CreatedBy)
            .Include(v => v.TargetPiller)
            .Include(v => v.OwningSuperAdmin)
            .Include(v => v.PillerApprovedBy)
            .Include(v => v.AdminApprovedBy)
            .Include(v => v.SuperAdminApprovedBy)
            .Include(v => v.RejectedBy)
            .AsQueryable();

        if (approvalInbox)
        {
            if (pillerApproverId.HasValue)
            {
                query = query.Where(v =>
                    v.TargetPillerId == pillerApproverId.Value);
            }
            else if (adminSharedApprovalAccess)
            {
                query = query.Where(v =>
                    v.Status != VoucherStatus.PendingPiller &&
                    v.Status != VoucherStatus.RejectedPiller);
            }
            else if (superAdminApproverId.HasValue)
            {
                query = query.Where(v =>
                    v.CurrentApprovalLevel == LeaveApprovalLevel.SuperAdmin ||
                    v.SuperAdminApprovedById != null ||
                    v.Status == VoucherStatus.Rejected);
            }
            else
            {
                query = query.Where(_ => false);
            }
        }

        // Filter by status
        if (statusFilter.HasValue)
        {
            query = query.Where(v => v.Status == statusFilter.Value);
        }

        // Filter by user
        if (userIdFilter.HasValue)
        {
            query = query.Where(v => v.CreatedById == userIdFilter.Value);
        }

        // Search by voucher number or title
        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            var term = searchTerm.ToLower();
            query = query.Where(v =>
                v.VoucherNumber.ToLower().Contains(term) ||
                v.Title.ToLower().Contains(term) ||
                (v.CreatedBy.FirstName + " " + v.CreatedBy.LastName).ToLower().Contains(term));
        }

        var totalCount = await query.CountAsync();

        // Sorting
        if (approvalInbox)
        {
            var pendingStatus = pillerApproverId.HasValue
                ? VoucherStatus.PendingPiller
                : adminSharedApprovalAccess
                    ? VoucherStatus.PendingAdmin
                    : VoucherStatus.PendingSuperAdmin;

            var prioritized = query.OrderBy(v => v.Status == pendingStatus ? 0 : 1);

            query = (sortColumn?.ToLower(), sortOrder?.ToLower()) switch
            {
                ("vouchernumber", "asc") => prioritized.ThenBy(v => v.VoucherNumber),
                ("vouchernumber", _) => prioritized.ThenByDescending(v => v.VoucherNumber),
                ("title", "asc") => prioritized.ThenBy(v => v.Title),
                ("title", _) => prioritized.ThenByDescending(v => v.Title),
                ("amount", "asc") => prioritized.ThenBy(v => v.Amount),
                ("amount", _) => prioritized.ThenByDescending(v => v.Amount),
                ("category", "asc") => prioritized.ThenBy(v => v.Category),
                ("category", _) => prioritized.ThenByDescending(v => v.Category),
                ("status", "asc") => prioritized.ThenBy(v => v.Status),
                ("status", _) => prioritized.ThenByDescending(v => v.Status),
                ("createdbyname", "asc") => prioritized.ThenBy(v => v.CreatedBy.FirstName),
                ("createdbyname", _) => prioritized.ThenByDescending(v => v.CreatedBy.FirstName),
                ("voucherdate", "asc") => prioritized.ThenBy(v => v.VoucherDate),
                ("voucherdate", _) => prioritized.ThenByDescending(v => v.VoucherDate),
                ("createdat", "asc") => prioritized.ThenBy(v => v.CreatedAt),
                _ => prioritized.ThenByDescending(v => v.CreatedAt),
            };
        }
        else
        {
            query = (sortColumn?.ToLower(), sortOrder?.ToLower()) switch
            {
                ("vouchernumber", "asc") => query.OrderBy(v => v.VoucherNumber),
                ("vouchernumber", _) => query.OrderByDescending(v => v.VoucherNumber),
                ("title", "asc") => query.OrderBy(v => v.Title),
                ("title", _) => query.OrderByDescending(v => v.Title),
                ("amount", "asc") => query.OrderBy(v => v.Amount),
                ("amount", _) => query.OrderByDescending(v => v.Amount),
                ("category", "asc") => query.OrderBy(v => v.Category),
                ("category", _) => query.OrderByDescending(v => v.Category),
                ("status", "asc") => query.OrderBy(v => v.Status),
                ("status", _) => query.OrderByDescending(v => v.Status),
                ("createdbyname", "asc") => query.OrderBy(v => v.CreatedBy.FirstName),
                ("createdbyname", _) => query.OrderByDescending(v => v.CreatedBy.FirstName),
                ("voucherdate", "asc") => query.OrderBy(v => v.VoucherDate),
                ("voucherdate", _) => query.OrderByDescending(v => v.VoucherDate),
                ("createdat", "asc") => query.OrderBy(v => v.CreatedAt),
                _ => query.OrderByDescending(v => v.CreatedAt),
            };
        }

        var vouchers = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (vouchers, totalCount);
    }

    public async Task<int> GetNextSequenceNumberAsync(int year, int month)
    {
        var prefix = $"VCH-{year}{month:D2}-";
        var lastVoucher = await _context.Vouchers
            .Where(v => v.VoucherNumber.StartsWith(prefix))
            .OrderByDescending(v => v.VoucherNumber)
            .FirstOrDefaultAsync();

        if (lastVoucher == null)
            return 1;

        var numberPart = lastVoucher.VoucherNumber.Substring(prefix.Length);
        return int.TryParse(numberPart, out var seq) ? seq + 1 : 1;
    }

    public async Task CreateAsync(Voucher voucher)
    {
        _context.Vouchers.Add(voucher);
        await _context.SaveChangesAsync();
    }

    public async Task UpdateAsync(Voucher voucher)
    {
        _context.Vouchers.Update(voucher);
        await _context.SaveChangesAsync();
    }
}
