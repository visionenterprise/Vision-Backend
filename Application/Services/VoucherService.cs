using vision_backend.Application.DTOs.Vouchers;
using vision_backend.Application.DTOs.Common;
using vision_backend.Application.Constants;
using vision_backend.Application.Interfaces;
using ClosedXML.Excel;
using System.Globalization;
using vision_backend.Domain.Entities;
using vision_backend.Domain.Enums;
using vision_backend.Infrastructure.Data;
using vision_backend.Infrastructure.Repositories;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace vision_backend.Application.Services;

public class VoucherService : IVoucherService
{
    private readonly IVoucherRepository _voucherRepository;
    private readonly IUserRepository _userRepository;
    private readonly IStorageService _storage;
    private readonly IUserService _userService;
    private readonly INotificationService _notificationService;
    private readonly ApplicationDbContext _context;

    public VoucherService(
        IVoucherRepository voucherRepository,
        IUserRepository userRepository,
        IStorageService storage,
        IUserService userService,
        INotificationService notificationService,
        ApplicationDbContext context)
    {
        _voucherRepository = voucherRepository;
        _userRepository = userRepository;
        _storage = storage;
        _userService = userService;
        _notificationService = notificationService;
        _context = context;
    }

    public async Task<VoucherResponse> CreateVoucherAsync(Guid creatorId, CreateVoucherRequest request)
    {
        var user = await _userRepository.GetByIdAsync(creatorId)
            ?? throw new InvalidOperationException("User not found.");
        var requesterRole = EffectiveRoleResolver.GetEffectiveRole(user);
        var canUseAdminFlow = requesterRole == UserRole.Admin && IsAccountsAdmin(user);

        Guid? targetPillerId = null;
        Guid? owningSuperAdminId = null;

        // Validate VoucherDate: Monday-to-Monday rule.
        // On Monday, users may submit for previous week (from previous Monday) up to today.
        // From Tuesday onward, only current week dates (from current Monday) are allowed.
        var today = DateTime.Now.Date;
        var voucherDate = request.VoucherDate.Date;

        // Calculate Monday of the current week
        // DayOfWeek.Sunday == 0 → daysSinceMonday = 6 (Sunday is the last day of the week)
        var daysSinceMonday = ((int)today.DayOfWeek - 1 + 7) % 7;
        var monday = today.AddDays(-daysSinceMonday);
        var earliestAllowedDate = today.DayOfWeek == DayOfWeek.Monday
            ? monday.AddDays(-7)
            : monday;

        if (voucherDate < earliestAllowedDate)
            throw new InvalidOperationException("Voucher date is outside the allowed submission window.");

        if (voucherDate > today)
            throw new InvalidOperationException("Voucher date cannot be in the future.");

        var now = DateTime.Now;
        var seq = await _voucherRepository.GetNextSequenceNumberAsync(now.Year, now.Month);
        var voucherNumber = $"VCH-{now.Year}{now.Month:D2}-{seq:D4}";

        if (requesterRole == UserRole.GeneralUser || (requesterRole == UserRole.Admin && !canUseAdminFlow))
        {
            if (!request.TargetPillerId.HasValue)
                throw new InvalidOperationException("A target piller must be selected for voucher request.");

            var piller = await _userRepository.GetByIdAsync(request.TargetPillerId.Value)
                ?? throw new InvalidOperationException("Selected piller not found.");

            if (!EffectiveRoleResolver.IsEffectivePiller(piller))
                throw new InvalidOperationException("Selected target user is not a piller.");

            if (!piller.SuperAdminId.HasValue)
                throw new InvalidOperationException("Selected piller has no assigned superadmin owner.");

            targetPillerId = piller.Id;
            owningSuperAdminId = piller.SuperAdminId;
        }
        else if (requesterRole == UserRole.Piller)
        {
            targetPillerId = user.Id;
            owningSuperAdminId = user.SuperAdminId;
        }
        else if (requesterRole == UserRole.Admin)
        {
            if (!user.SuperAdminId.HasValue)
                throw new InvalidOperationException("Admin has no superadmin owner assigned.");

            owningSuperAdminId = user.SuperAdminId;
        }
        else if (requesterRole == UserRole.SuperAdmin)
        {
            owningSuperAdminId = user.Id;
        }

        var initialLevel = requesterRole == UserRole.SuperAdmin
            ? LeaveApprovalLevel.SuperAdmin
            : requesterRole == UserRole.Admin && canUseAdminFlow
                ? LeaveApprovalLevel.SuperAdmin
                : requesterRole == UserRole.Piller
                    ? LeaveApprovalLevel.Admin
                    : LeaveApprovalLevel.Piller;

        var initialStatus = requesterRole == UserRole.SuperAdmin
            ? VoucherStatus.Approved
            : requesterRole == UserRole.Admin && canUseAdminFlow
                ? VoucherStatus.PendingSuperAdmin
                : requesterRole == UserRole.Piller
                    ? VoucherStatus.PendingAdmin
                    : VoucherStatus.PendingPiller;

        var voucher = new Voucher
        {
            Id = Guid.NewGuid(),
            VoucherNumber = voucherNumber,
            Title = request.Title,
            Description = request.Description,
            Amount = request.Amount,
            VoucherDate = voucherDate,
            Category = request.Category,
            SiteName = request.SiteName,
            Status = initialStatus,
            CurrentApprovalLevel = initialLevel,
            TargetPillerId = targetPillerId,
            OwningSuperAdminId = owningSuperAdminId,
            CreatedById = creatorId,
            CreatedAt = now,
            UpdatedAt = now,
        };

        if (requesterRole == UserRole.SuperAdmin)
        {
            voucher.SuperAdminApprovedById = user.Id;
            voucher.SuperAdminApprovedAt = now;
        }

        await _voucherRepository.CreateAsync(voucher);

        // Re-fetch with includes
        var created = await _voucherRepository.GetByIdAsync(voucher.Id)
            ?? throw new InvalidOperationException("Failed to retrieve created voucher.");

        await _notificationService.NotifyVoucherApprovalPendingAsync(
            voucher.Id,
            voucher.VoucherNumber,
            creatorId,
            voucher.CurrentApprovalLevel,
            voucher.TargetPillerId,
            voucher.OwningSuperAdminId);

        return MapToResponse(created);
    }

    public async Task<VoucherResponse> ApproveVoucherAsync(Guid voucherId, Guid approverId, UserRole approverRole)
    {
        var approver = await _userRepository.GetByIdAsync(approverId)
            ?? throw new InvalidOperationException("Approver user not found.");
        var effectiveApproverRole = EffectiveRoleResolver.GetEffectiveRole(approver);

        var voucher = await _voucherRepository.GetByIdAsync(voucherId)
            ?? throw new InvalidOperationException("Voucher not found.");

        if (voucher.Status == VoucherStatus.Approved)
            throw new InvalidOperationException("Voucher is already fully approved.");

        if (voucher.Status == VoucherStatus.Rejected || voucher.Status == VoucherStatus.RejectedAdmin || voucher.Status == VoucherStatus.RejectedPiller)
            throw new InvalidOperationException("Cannot approve a rejected voucher.");

        var now = DateTime.Now;

        if (effectiveApproverRole == UserRole.Piller)
        {
            if (voucher.CurrentApprovalLevel != LeaveApprovalLevel.Piller)
                throw new InvalidOperationException("Voucher is not waiting for Piller approval.");

            if (voucher.TargetPillerId != approverId)
                throw new InvalidOperationException("This voucher is assigned to a different piller.");

            if (!await _userService.HasPermissionAsync(approverId, PermissionSlugs.VoucherManagement))
                throw new InvalidOperationException("Piller does not have voucher_management permission.");

            voucher.PillerApprovedById = approverId;
            voucher.PillerApprovedAt = now;
            voucher.Status = VoucherStatus.PendingAdmin;
            voucher.CurrentApprovalLevel = LeaveApprovalLevel.Admin;
        }
        else if (effectiveApproverRole == UserRole.Admin)
        {
            if (voucher.CurrentApprovalLevel != LeaveApprovalLevel.Admin)
                throw new InvalidOperationException("Voucher is not waiting for Admin approval.");

            if (!IsAccountsAdmin(approver))
                throw new InvalidOperationException("Only Accounts Admin can approve vouchers at admin stage.");

            if (!await _userService.HasPermissionAsync(approverId, PermissionSlugs.VoucherManagement))
                throw new InvalidOperationException("Admin does not have voucher_management permission.");

            voucher.AdminApprovedById = approverId;
            voucher.AdminApprovedAt = now;
            voucher.Status = VoucherStatus.PendingSuperAdmin;
            voucher.CurrentApprovalLevel = LeaveApprovalLevel.SuperAdmin;
        }
        else if (effectiveApproverRole == UserRole.SuperAdmin)
        {
            if (voucher.CurrentApprovalLevel != LeaveApprovalLevel.SuperAdmin)
                throw new InvalidOperationException("Voucher is not waiting for Super Admin approval.");

            voucher.SuperAdminApprovedById = approverId;
            voucher.SuperAdminApprovedAt = now;
            voucher.Status = VoucherStatus.Approved;
            await DeductBalanceAsync(voucher, now);
        }
        else
        {
            throw new InvalidOperationException("Only Admin or Super Admin can approve vouchers.");
        }

        voucher.UpdatedAt = now;
        await _voucherRepository.UpdateAsync(voucher);

        var updated = await _voucherRepository.GetByIdAsync(voucherId)
            ?? throw new InvalidOperationException("Failed to retrieve updated voucher.");

        await _notificationService.NotifyVoucherDecisionAsync(updated.Id, updated.VoucherNumber, updated.CreatedById, updated.Status);

        if (updated.Status != VoucherStatus.Approved)
        {
            await _notificationService.NotifyVoucherApprovalPendingAsync(
                updated.Id,
                updated.VoucherNumber,
                updated.CreatedById,
                updated.CurrentApprovalLevel,
                updated.TargetPillerId,
                updated.OwningSuperAdminId);
        }

        return MapToResponse(updated);
    }

    public async Task<VoucherResponse> RejectVoucherAsync(Guid voucherId, Guid rejecterId, UserRole role, string reason)
    {
        var rejecter = await _userRepository.GetByIdAsync(rejecterId)
            ?? throw new InvalidOperationException("Rejecter user not found.");
        var effectiveRole = EffectiveRoleResolver.GetEffectiveRole(rejecter);

        if (effectiveRole != UserRole.Piller && effectiveRole != UserRole.Admin && effectiveRole != UserRole.SuperAdmin)
            throw new InvalidOperationException("Only Piller, Admin, or Super Admin can reject vouchers.");

        var voucher = await _voucherRepository.GetByIdAsync(voucherId)
            ?? throw new InvalidOperationException("Voucher not found.");

        if (voucher.Status == VoucherStatus.Approved)
            throw new InvalidOperationException("Cannot reject an already approved voucher.");

        if (voucher.Status == VoucherStatus.Rejected || voucher.Status == VoucherStatus.RejectedAdmin || voucher.Status == VoucherStatus.RejectedPiller)
            throw new InvalidOperationException("Voucher is already rejected.");

        if (effectiveRole == UserRole.Piller)
        {
            if (voucher.CurrentApprovalLevel != LeaveApprovalLevel.Piller)
                throw new InvalidOperationException("Voucher is not waiting for Piller action.");
            if (voucher.TargetPillerId != rejecterId)
                throw new InvalidOperationException("This voucher is assigned to a different piller.");
            if (!await _userService.HasPermissionAsync(rejecterId, PermissionSlugs.VoucherManagement))
                throw new InvalidOperationException("Piller does not have voucher_management permission.");
            voucher.Status = VoucherStatus.RejectedPiller;
        }
        else if (effectiveRole == UserRole.Admin)
        {
            if (voucher.CurrentApprovalLevel != LeaveApprovalLevel.Admin)
                throw new InvalidOperationException("Voucher is not waiting for Admin action.");
            if (!IsAccountsAdmin(rejecter))
                throw new InvalidOperationException("Only Accounts Admin can reject vouchers at admin stage.");
            if (!await _userService.HasPermissionAsync(rejecterId, PermissionSlugs.VoucherManagement))
                throw new InvalidOperationException("Admin does not have voucher_management permission.");
            voucher.Status = VoucherStatus.RejectedAdmin;
        }
        else
        {
            if (voucher.CurrentApprovalLevel != LeaveApprovalLevel.SuperAdmin)
                throw new InvalidOperationException("Voucher is not waiting for SuperAdmin action.");
            voucher.Status = VoucherStatus.Rejected;
        }

        var now = DateTime.Now;
        voucher.RejectedById = rejecterId;
        voucher.RejectedAt = now;
        voucher.RejectionReason = reason;
        voucher.UpdatedAt = now;

        await _voucherRepository.UpdateAsync(voucher);

        var updated = await _voucherRepository.GetByIdAsync(voucherId)
            ?? throw new InvalidOperationException("Failed to retrieve updated voucher.");

        await _notificationService.NotifyVoucherDecisionAsync(updated.Id, updated.VoucherNumber, updated.CreatedById, updated.Status);

        return MapToResponse(updated);
    }

    public async Task<VoucherResponse> ReopenVoucherAsync(Guid voucherId, Guid actorId, UserRole role)
    {
        var actor = await _userRepository.GetByIdAsync(actorId)
            ?? throw new InvalidOperationException("Actor user not found.");
        var effectiveRole = EffectiveRoleResolver.GetEffectiveRole(actor);

        if (effectiveRole != UserRole.SuperAdmin)
            throw new InvalidOperationException("Only super admin can reopen vouchers.");

        var voucher = await _voucherRepository.GetByIdAsync(voucherId)
            ?? throw new InvalidOperationException("Voucher not found.");

        if (voucher.Status != VoucherStatus.Rejected)
        {
            throw new InvalidOperationException("Only superadmin-rejected vouchers can be reopened.");
        }

        if (!voucher.RejectedAt.HasValue)
            throw new InvalidOperationException("Reopen window is unavailable because rejection timestamp is missing.");

        var now = DateTime.Now;
        var reopenDeadline = voucher.RejectedAt.Value.AddDays(7);
        if (now > reopenDeadline)
            throw new InvalidOperationException("Reopen is allowed only within one week of superadmin rejection.");

        voucher.Status = VoucherStatus.PendingSuperAdmin;
        voucher.CurrentApprovalLevel = LeaveApprovalLevel.SuperAdmin;

        voucher.RejectedById = null;
        voucher.RejectedAt = null;
        voucher.RejectionReason = null;
        voucher.UpdatedAt = now;

        await _voucherRepository.UpdateAsync(voucher);

        var updated = await _voucherRepository.GetByIdAsync(voucherId)
            ?? throw new InvalidOperationException("Failed to retrieve updated voucher.");

        return MapToResponse(updated);
    }

    public async Task<VoucherResponse> GetVoucherAsync(Guid voucherId)
    {
        var voucher = await _voucherRepository.GetByIdAsync(voucherId)
            ?? throw new InvalidOperationException("Voucher not found.");

        return MapToResponse(voucher);
    }

    public async Task<List<VoucherResponse>> GetUserVouchersAsync(Guid userId)
    {
        var vouchers = await _voucherRepository.GetByUserIdAsync(userId);
        return vouchers.Select(MapToResponse).ToList();
    }

    public async Task<PagedResult<VoucherResponse>> GetVouchersPagedAsync(
        VoucherSearchRequest request, Guid? currentUserId, UserRole role)
    {
        var isApprovalInbox = request.ApprovalInbox;

        Guid? userIdFilter = null;
        Guid? pillerApproverId = null;
        var adminSharedApprovalAccess = false;
        Guid? superAdminApproverId = null;

        if (isApprovalInbox)
        {
            if (!currentUserId.HasValue)
                throw new InvalidOperationException("Current user context is required.");

            var actor = await _userRepository.GetByIdAsync(currentUserId.Value)
                ?? throw new InvalidOperationException("Current user not found.");
            var effectiveRole = EffectiveRoleResolver.GetEffectiveRole(actor);

            if (effectiveRole == UserRole.Piller)
            {
                pillerApproverId = actor.Id;
            }
            else if (effectiveRole == UserRole.Admin)
            {
                var hasVoucherManagement = await _userService.HasPermissionAsync(actor.Id, PermissionSlugs.VoucherManagement);
                if (hasVoucherManagement && IsAccountsAdmin(actor))
                {
                    adminSharedApprovalAccess = true;
                }
            }
            else if (effectiveRole == UserRole.SuperAdmin)
            {
                superAdminApproverId = actor.Id;
            }

            // Allow approvals view filters by creator when provided.
            if (request.UserIdFilter.HasValue)
            {
                userIdFilter = request.UserIdFilter.Value;
            }
        }
        else
        {
            // "My vouchers" view should always show current user's own vouchers only.
            userIdFilter = currentUserId;
        }

        var (vouchers, totalCount) = await _voucherRepository.GetPagedAsync(
            request.Page,
            request.PageSize,
            request.SearchTerm,
            request.SortColumn,
            request.SortOrder,
            request.StatusFilter,
            userIdFilter,
            isApprovalInbox,
            pillerApproverId,
            adminSharedApprovalAccess,
            superAdminApproverId);

        return new PagedResult<VoucherResponse>
        {
            Items = vouchers.Select(MapToResponse).ToList(),
            TotalCount = totalCount,
            Page = request.Page,
            PageSize = request.PageSize,
        };
    }

    public async Task<VoucherResponse> UploadReceiptsAsync(
        Guid voucherId, Guid userId,
        List<(Stream Stream, string FileName, string ContentType)> files)
    {
        try
        {
            var voucher = await _voucherRepository.GetByIdAsync(voucherId)
                ?? throw new InvalidOperationException("Voucher not found.");

            if (voucher.CreatedById != userId)
                throw new InvalidOperationException("You can only upload receipts for your own vouchers.");

            // Initialize the key list from current DB value (may be null)
            var keys = voucher.ReceiptUrls != null
                ? new List<string>(voucher.ReceiptUrls)
                : new List<string>();

            // Upload each file in parallel — key pattern: receipts/{voucherId}/{guid}{ext}
            Console.WriteLine($"[Upload Init] Starting upload of {files.Count} files for voucher {voucherId}");
            var uploadTasks = files.Select(async file =>
            {
                var ext = Path.GetExtension(file.FileName);
                var objectKey = $"receipts/{voucherId}/{Guid.NewGuid():N}{ext}";
                try
                {
                    Console.WriteLine($"[GCS Upload] Uploading {file.FileName} to {objectKey}");
                    var storedKey = await _storage.UploadAsync(file.Stream, objectKey, file.ContentType);
                    Console.WriteLine($"[GCS Success] {storedKey} uploaded successfully");
                    return storedKey;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[GCS Error] Failed to upload {objectKey}: {ex.GetType().Name}: {ex.Message}");
                    Console.WriteLine($"[GCS Stack] {ex.StackTrace}");
                    throw;
                }
            });

            var uploadedKeys = await Task.WhenAll(uploadTasks);
            keys.AddRange(uploadedKeys);

            // Reassign to trigger EF Core change detection on the array column
            Console.WriteLine($"[DB Update] Updating voucher {voucherId} with {uploadedKeys.Length} receipt URLs");
            voucher.ReceiptUrls = keys;
            voucher.UpdatedAt = DateTime.Now;
            await _voucherRepository.UpdateAsync(voucher);

            Console.WriteLine($"[Upload Complete] Voucher {voucherId} updated successfully");
            return MapToResponse(voucher);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[UploadReceiptsAsync Error] {ex.GetType().Name}: {ex.Message}");
            Console.WriteLine($"[Stack] {ex.StackTrace}");
            throw;
        }
    }

    public async Task<(byte[] Content, string FileName)> ExportVouchersAsync(VoucherExportRequest request, Guid requesterId, UserRole requesterRole)
    {
        var targetUserId = await ResolveExportTargetUserIdAsync(request, requesterId, requesterRole);

        var fromDate = request.FromDate.Date;
        var toDate = request.ToDate.Date;

        if (fromDate > toDate)
            throw new InvalidOperationException("From date cannot be after To date.");

        var selectedUser = await _userRepository.GetByIdAsync(targetUserId)
            ?? throw new InvalidOperationException("Selected user not found.");
        var statementRows = await BuildStatementRowsAsync(targetUserId, fromDate, toDate);

        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Vouchers");

        worksheet.Range("A1:I1").Merge().Value = "VISION ENTERPRISE";
        worksheet.Range("A2:I2").Merge().Value = "OFFICE/SITE EXPENCE";
        worksheet.Range("A3:I3").Merge().Value = $"NAME :- {selectedUser.FirstName} {selectedUser.LastName}";

        worksheet.Range("A1:I3").Style.Font.Bold = true;
        worksheet.Range("A1:I3").Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        worksheet.Range("A1:I3").Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        worksheet.Row(1).Height = 24;
        worksheet.Row(2).Height = 20;
        worksheet.Row(3).Height = 20;

        worksheet.Cell("A5").Value = "NO";
        worksheet.Cell("B5").Value = "DATE OF EXPENCE";
        worksheet.Cell("C5").Value = "TITLE";
        worksheet.Cell("D5").Value = "SITE NAME";
        worksheet.Cell("E5").Value = "CATEGORY";
        worksheet.Cell("F5").Value = "APPROVAL DATE";
        worksheet.Cell("G5").Value = "AMT. DEBITED";
        worksheet.Cell("H5").Value = "AMT. CREDITED";
        worksheet.Cell("I5").Value = "ACCOUNT BALANCE";

        var headerRange = worksheet.Range("A5:I5");
        headerRange.Style.Font.Bold = true;
        headerRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        headerRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        headerRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#F3F4F6");

        var rowNumber = 6;
        foreach (var exportRow in statementRows)
        {
            worksheet.Cell(rowNumber, 1).Value = exportRow.Sequence;

            if (exportRow.EntryDate.HasValue)
            {
                worksheet.Cell(rowNumber, 2).Value = exportRow.EntryDate.Value;
                worksheet.Cell(rowNumber, 2).Style.DateFormat.Format = "dd-MMM-yyyy";
            }

            worksheet.Cell(rowNumber, 3).Value = exportRow.Description;
            worksheet.Cell(rowNumber, 4).Value = exportRow.SiteName;
            worksheet.Cell(rowNumber, 5).Value = exportRow.Category;

            if (exportRow.ApprovalDate.HasValue)
            {
                worksheet.Cell(rowNumber, 6).Value = exportRow.ApprovalDate.Value;
                worksheet.Cell(rowNumber, 6).Style.DateFormat.Format = "dd-MMM-yyyy";
            }

            if (exportRow.DebitedAmount.HasValue)
            {
                worksheet.Cell(rowNumber, 7).Value = exportRow.DebitedAmount.Value;
                worksheet.Cell(rowNumber, 7).Style.NumberFormat.Format = "#,##0.00";
            }

            if (exportRow.CreditedAmount.HasValue)
            {
                worksheet.Cell(rowNumber, 8).Value = exportRow.CreditedAmount.Value;
                worksheet.Cell(rowNumber, 8).Style.NumberFormat.Format = "#,##0.00";
            }

            worksheet.Cell(rowNumber, 9).Value = exportRow.RemainingAmount;
            worksheet.Cell(rowNumber, 9).Style.NumberFormat.Format = "#,##0.00";

            if (exportRow.IsOpeningOrClosingRow)
            {
                worksheet.Cell(rowNumber, 3).Style.Font.Bold = true;
                worksheet.Cell(rowNumber, 9).Style.Font.Bold = true;
                worksheet.Range(rowNumber, 1, rowNumber, 9).Style.Fill.BackgroundColor = XLColor.FromHtml("#F3F4F6");
            }

            rowNumber++;
        }

        if (statementRows.Count > 0)
        {
            var dataRange = worksheet.Range(6, 1, rowNumber - 1, 9);
            dataRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            dataRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
            dataRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            dataRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }

        worksheet.Columns(1, 9).AdjustToContents();
        worksheet.Column(3).Width = Math.Max(worksheet.Column(3).Width, 30);
        worksheet.Column(4).Width = Math.Max(worksheet.Column(4).Width, 18);
        worksheet.Column(5).Width = Math.Max(worksheet.Column(5).Width, 16);

        var fullRange = worksheet.Range(5, 1, rowNumber - 1, 9);
        fullRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        fullRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        fullRange.Style.Alignment.WrapText = true;

        worksheet.PageSetup.PaperSize = XLPaperSize.A4Paper;
        worksheet.PageSetup.PageOrientation = XLPageOrientation.Portrait;
        worksheet.PageSetup.FitToPages(1, 0);
        worksheet.PageSetup.Margins.Left = 0.4;
        worksheet.PageSetup.Margins.Right = 0.4;
        worksheet.PageSetup.Margins.Top = 0.75;
        worksheet.PageSetup.Margins.Bottom = 0.75;

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);

        var safeUser = $"{selectedUser.FirstName}_{selectedUser.LastName}".Replace(" ", "_");
        var fileName = $"voucher_statement_{safeUser}_{fromDate:yyyyMMdd}_{toDate:yyyyMMdd}.xlsx";

        return (stream.ToArray(), fileName);
    }

    public async Task<(byte[] Content, string FileName)> ExportVouchersPdfAsync(VoucherExportRequest request, Guid requesterId, UserRole requesterRole)
    {
        var targetUserId = await ResolveExportTargetUserIdAsync(request, requesterId, requesterRole);

        var fromDate = request.FromDate.Date;
        var toDate = request.ToDate.Date;

        if (fromDate > toDate)
            throw new InvalidOperationException("From date cannot be after To date.");

        var selectedUser = await _userRepository.GetByIdAsync(targetUserId)
            ?? throw new InvalidOperationException("Selected user not found.");
        var statementRows = await BuildStatementRowsAsync(targetUserId, fromDate, toDate);
        var safeUser = $"{selectedUser.FirstName}_{selectedUser.LastName}".Replace(" ", "_");
        var fileName = $"voucher_statement_{safeUser}_{fromDate:yyyyMMdd}_{toDate:yyyyMMdd}.pdf";

        QuestPDF.Settings.License = LicenseType.Community;

        var pdfBytes = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(1.5f, Unit.Centimetre);
                page.DefaultTextStyle(t => t.FontSize(8).FontFamily("Times New Roman"));

                page.Content().Column(col =>
                {
                    // Full table: header block + column headers + data rows
                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(cols =>
                        {
                            cols.ConstantColumn(25);   // NO
                            cols.ConstantColumn(62);   // DATE
                            cols.RelativeColumn(3);    // DESCRIPTION
                            cols.RelativeColumn(2);    // SITE NAME
                            cols.RelativeColumn(1.5f); // CATEGORY
                            cols.ConstantColumn(62);   // APPROVAL DATE
                            cols.ConstantColumn(64);   // AMT. DEBITED
                            cols.ConstantColumn(64);   // AMT. CREDITED
                            cols.ConstantColumn(70);   // ACCOUNT BALANCE
                        });

                        // Header title rows (merged across all 9 columns)
                        table.Cell().ColumnSpan(9)
                            .Border(1f).BorderColor("#000000")
                            .Padding(6).AlignCenter().AlignMiddle()
                            .Text("VISION ENTERPRISE").Bold().FontSize(13);

                        table.Cell().ColumnSpan(9)
                            .Border(1f).BorderColor("#000000")
                            .Padding(4).AlignCenter().AlignMiddle()
                            .Text("OFFICE/SITE EXPENCE").Bold().FontSize(10);

                        table.Cell().ColumnSpan(9)
                            .Border(1f).BorderColor("#000000")
                            .Padding(4).AlignCenter().AlignMiddle()
                            .Text($"NAME :- {selectedUser.FirstName} {selectedUser.LastName}   " +
                                  $"PERIOD :- {fromDate:dd-MMM-yyyy} TO {toDate:dd-MMM-yyyy}").Bold().FontSize(9);

                        static IContainer HeaderCell(IContainer c) =>
                            c.DefaultTextStyle(t => t.Bold())
                             .Background("#E5E7EB")
                             .Border(1f)
                             .BorderColor("#000000")
                             .Padding(4)
                             .AlignCenter()
                             .AlignMiddle();

                        // Column header row (plain cells, no table.Header so order is guaranteed)
                        table.Cell().Element(HeaderCell).Text("NO");
                        table.Cell().Element(HeaderCell).Text("DATE OF EXPENCE");
                        table.Cell().Element(HeaderCell).Text("TITLE");
                        table.Cell().Element(HeaderCell).Text("SITE NAME");
                        table.Cell().Element(HeaderCell).Text("CATEGORY");
                        table.Cell().Element(HeaderCell).Text("APPROVAL DATE");
                        table.Cell().Element(HeaderCell).Text("AMT. DEBITED");
                        table.Cell().Element(HeaderCell).Text("AMT. CREDITED");
                        table.Cell().Element(HeaderCell).Text("ACCOUNT BALANCE");

                        static IContainer DataCell(IContainer c) =>
                            c.Border(1f)
                             .BorderColor("#000000")
                             .Padding(4)
                             .AlignCenter()
                             .AlignMiddle();

                        static IContainer DataCellLeft(IContainer c) =>
                            c.Border(1f)
                             .BorderColor("#000000")
                             .Padding(4)
                             .AlignLeft()
                             .AlignMiddle();

                        for (var i = 0; i < statementRows.Count; i++)
                        {
                            var row = statementRows[i];

                            IContainer CellStyle(IContainer c)
                            {
                                var baseCell = DataCell(c);
                                return row.IsOpeningOrClosingRow
                                    ? baseCell.Background("#F3F4F6").DefaultTextStyle(t => t.Bold())
                                    : baseCell;
                            }

                            IContainer CellStyleLeft(IContainer c)
                            {
                                var baseCell = DataCellLeft(c);
                                return row.IsOpeningOrClosingRow
                                    ? baseCell.Background("#F3F4F6").DefaultTextStyle(t => t.Bold())
                                    : baseCell;
                            }

                            table.Cell().Element(CellStyle).Text(row.Sequence);
                            table.Cell().Element(CellStyle).Text(row.EntryDate.HasValue ? row.EntryDate.Value.ToString("dd-MMM-yyyy") : "");
                            table.Cell().Element(CellStyleLeft).Text(row.Description ?? "");
                            table.Cell().Element(CellStyle).Text(row.SiteName ?? "");
                            table.Cell().Element(CellStyle).Text(row.Category ?? "");
                            table.Cell().Element(CellStyle).Text(row.ApprovalDate.HasValue ? row.ApprovalDate.Value.ToString("dd-MMM-yyyy") : "");
                            table.Cell().Element(CellStyle).Text(row.DebitedAmount.HasValue ? row.DebitedAmount.Value.ToString("N2") : "").AlignRight();
                            table.Cell().Element(CellStyle).Text(row.CreditedAmount.HasValue ? row.CreditedAmount.Value.ToString("N2") : "").AlignRight();
                            table.Cell().Element(CellStyle).Text(row.RemainingAmount.ToString("N2")).AlignRight();
                        }
                    });
                });
            });
        }).GeneratePdf();

        return (pdfBytes, fileName);
    }

    private static string WrapTextAfterNWords(string text, int wordsPerLine)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        var words = text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        var lines = new List<string>();
        var currentLine = new List<string>();

        foreach (var word in words)
        {
            currentLine.Add(word);
            if (currentLine.Count == wordsPerLine)
            {
                lines.Add(string.Join(" ", currentLine));
                currentLine.Clear();
            }
        }

        if (currentLine.Count > 0)
        {
            lines.Add(string.Join(" ", currentLine));
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string ConvertAmountToWords(decimal amount)
    {
        var roundedAmount = (long)Math.Round(amount, MidpointRounding.AwayFromZero);
        if (roundedAmount == 0)
            return "ZERO RUPEES";

        return $"{ConvertNumberToWords(roundedAmount)} RUPEES";
    }

    private static string ConvertNumberToWords(long number)
    {
        if (number == 0)
            return "ZERO";

        var parts = new List<string>();

        void AddPart(long divisor, string label)
        {
            var quotient = number / divisor;
            if (quotient > 0)
            {
                parts.Add($"{ConvertBelowThousand((int)quotient)} {label}".Trim());
                number %= divisor;
            }
        }

        AddPart(10000000, "CRORE");
        AddPart(100000, "LAKH");
        AddPart(1000, "THOUSAND");

        if (number > 0)
        {
            parts.Add(ConvertBelowThousand((int)number));
        }

        return string.Join(' ', parts).Trim();
    }

    private static string ConvertBelowThousand(int number)
    {
        string[] ones =
        {
            "", "ONE", "TWO", "THREE", "FOUR", "FIVE", "SIX", "SEVEN", "EIGHT", "NINE",
            "TEN", "ELEVEN", "TWELVE", "THIRTEEN", "FOURTEEN", "FIFTEEN", "SIXTEEN", "SEVENTEEN", "EIGHTEEN", "NINETEEN"
        };
        string[] tens = { "", "", "TWENTY", "THIRTY", "FORTY", "FIFTY", "SIXTY", "SEVENTY", "EIGHTY", "NINETY" };

        var words = new List<string>();
        if (number >= 100)
        {
            words.Add($"{ones[number / 100]} HUNDRED");
            number %= 100;
        }

        if (number >= 20)
        {
            words.Add(tens[number / 10]);
            number %= 10;
        }

        if (number > 0)
        {
            words.Add(ones[number]);
        }

        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(string.Join(' ', words).ToLowerInvariant()).ToUpperInvariant();
    }

    private async Task<Guid> ResolveExportTargetUserIdAsync(VoucherExportRequest request, Guid requesterId, UserRole requesterRole)
    {
        var requester = await _userRepository.GetByIdAsync(requesterId)
            ?? throw new InvalidOperationException("Requesting user not found.");

        if (requesterRole == UserRole.Admin || requesterRole == UserRole.SuperAdmin)
        {
            if (!request.UserId.HasValue || request.UserId.Value == Guid.Empty)
                throw new InvalidOperationException("User is required for export.");

            var target = await _userRepository.GetByIdAsync(request.UserId.Value)
                ?? throw new InvalidOperationException("Selected user not found.");

            if (requesterRole == UserRole.Admin)
            {
                if (!await _userService.HasPermissionAsync(requesterId, PermissionSlugs.VoucherManagement))
                    throw new InvalidOperationException("Admin does not have voucher_management permission.");

                if (target.Role == UserRole.SuperAdmin)
                    throw new InvalidOperationException("Admin cannot export superadmin statements.");

                return target.Id;
            }
            return target.Id;
        }

        return requesterId;
    }

    private async Task<List<StatementExportRow>> BuildStatementRowsAsync(Guid userId, DateTime fromDate, DateTime toDate)
    {
        var fromStart = fromDate.Date;
        var toExclusive = toDate.Date.AddDays(1);

        var openingBalance = await GetBalanceBeforeAsync(userId, fromStart);

        var ledgerTransactions = await _context.BalanceTransactions
            .Where(t => t.UserId == userId && t.EntryDate >= fromStart && t.EntryDate < toExclusive)
            .OrderBy(t => t.EntryDate)
            .ThenBy(t => t.Id)
            .ToListAsync();

        var ledgerVoucherIds = await _context.BalanceTransactions
            .Where(t => t.UserId == userId && t.ReferenceType == "VoucherApproval" && t.ReferenceId.HasValue && t.EntryDate < toExclusive)
            .Select(t => t.ReferenceId!.Value)
            .Distinct()
            .ToListAsync();

        var legacyApprovedVouchers = await _context.Vouchers
            .Where(v => v.CreatedById == userId
                && v.Status == VoucherStatus.Approved
                && (v.SuperAdminApprovedAt ?? v.UpdatedAt) < toExclusive
                && !ledgerVoucherIds.Contains(v.Id))
            .OrderBy(v => v.SuperAdminApprovedAt ?? v.UpdatedAt)
            .ThenBy(v => v.CreatedAt)
            .ToListAsync();

        var legacyBeforeRange = legacyApprovedVouchers
            .Where(v => (v.SuperAdminApprovedAt ?? v.UpdatedAt) < fromStart)
            .Sum(v => v.Amount);
        openingBalance -= legacyBeforeRange;

        var voucherIds = ledgerTransactions
            .Where(t => t.ReferenceType == "VoucherApproval" && t.ReferenceId.HasValue)
            .Select(t => t.ReferenceId!.Value)
            .Distinct()
            .ToList();

        var vouchers = await _context.Vouchers
            .Where(v => voucherIds.Contains(v.Id))
            .ToDictionaryAsync(v => v.Id, v => v);

        var events = new List<StatementExportEvent>();

        foreach (var transaction in ledgerTransactions)
        {
            var eventRow = new StatementExportEvent
            {
                SortDate = transaction.EntryDate,
                Description = transaction.Description,
                Type = transaction.Type,
                Amount = transaction.Amount,
            };

            if (transaction.Type == BalanceTransactionType.Debit
                && transaction.ReferenceType == "VoucherApproval"
                && transaction.ReferenceId.HasValue
                && vouchers.TryGetValue(transaction.ReferenceId.Value, out var voucher))
            {
                eventRow.EntryDate = voucher.VoucherDate.Date;
                eventRow.Description = WrapTextAfterNWords(voucher.Title, 10);
                eventRow.SiteName = voucher.SiteName;
                eventRow.Category = ResolveCategoryName(voucher.Category);
                eventRow.ApprovalDate = (voucher.SuperAdminApprovedAt ?? voucher.AdminApprovedAt)?.Date;
                eventRow.SortDate = voucher.SuperAdminApprovedAt ?? voucher.UpdatedAt;
            }
            else
            {
                eventRow.EntryDate = transaction.EntryDate.Date;
            }

            events.Add(eventRow);
        }

        foreach (var voucher in legacyApprovedVouchers.Where(v => (v.SuperAdminApprovedAt ?? v.UpdatedAt) >= fromStart))
        {
            events.Add(new StatementExportEvent
            {
                SortDate = voucher.SuperAdminApprovedAt ?? voucher.UpdatedAt,
                EntryDate = voucher.VoucherDate.Date,
                Description = WrapTextAfterNWords(voucher.Title, 10),
                SiteName = voucher.SiteName,
                Category = ResolveCategoryName(voucher.Category),
                ApprovalDate = (voucher.SuperAdminApprovedAt ?? voucher.AdminApprovedAt)?.Date,
                Type = BalanceTransactionType.Debit,
                Amount = voucher.Amount,
            });
        }

        events = events
            .OrderBy(e => e.SortDate)
            .ThenBy(e => e.Type == BalanceTransactionType.Credit ? 0 : 1)
            .ToList();

        var rows = new List<StatementExportRow>();
        var runningBalance = openingBalance;

        rows.Add(new StatementExportRow
        {
            Sequence = string.Empty,
            EntryDate = fromStart,
            Description = "Starting Balance",
            RemainingAmount = runningBalance,
            IsOpeningOrClosingRow = true,
        });

        var sequence = 1;
        foreach (var entry in events)
        {
            var row = new StatementExportRow
            {
                Sequence = sequence.ToString(),
                EntryDate = entry.EntryDate,
                Description = entry.Description,
                SiteName = entry.SiteName,
                Category = entry.Category,
                ApprovalDate = entry.ApprovalDate,
            };

            if (entry.Type == BalanceTransactionType.Debit)
            {
                row.DebitedAmount = entry.Amount;
                runningBalance -= entry.Amount;
            }
            else
            {
                row.CreditedAmount = entry.Amount;
                runningBalance += entry.Amount;
            }

            row.RemainingAmount = runningBalance;
            rows.Add(row);
            sequence++;
        }

        rows.Add(new StatementExportRow
        {
            Sequence = string.Empty,
            EntryDate = toDate,
            Description = "Closing Balance",
            RemainingAmount = runningBalance,
            IsOpeningOrClosingRow = true,
        });

        return rows;
    }

    private async Task<decimal> GetBalanceBeforeAsync(Guid userId, DateTime beforeDate)
    {
        var credits = await _context.BalanceTransactions
            .Where(t => t.UserId == userId && t.EntryDate < beforeDate && t.Type == BalanceTransactionType.Credit)
            .SumAsync(t => (decimal?)t.Amount) ?? 0m;

        var debits = await _context.BalanceTransactions
            .Where(t => t.UserId == userId && t.EntryDate < beforeDate && t.Type == BalanceTransactionType.Debit)
            .SumAsync(t => (decimal?)t.Amount) ?? 0m;

        return credits - debits;
    }

    private static DateTime StartOfWeekMonday(DateTime date)
    {
        var daysSinceMonday = ((int)date.DayOfWeek - 1 + 7) % 7;
        return date.AddDays(-daysSinceMonday);
    }

    private async Task DeductBalanceAsync(Voucher voucher, DateTime entryDate)
    {
        var user = await _userRepository.GetByIdAsync(voucher.CreatedById)
            ?? throw new InvalidOperationException("Voucher creator not found.");

        user.Balance -= voucher.Amount;
        await _userRepository.UpdateAsync(user);

        _context.BalanceTransactions.Add(new BalanceTransaction
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Type = BalanceTransactionType.Debit,
            Amount = voucher.Amount,
            BalanceAfter = user.Balance,
            EntryDate = entryDate,
            Description = "Voucher expense",
            ReferenceType = "VoucherApproval",
            ReferenceId = voucher.Id,
            CreatedById = voucher.SuperAdminApprovedById,
        });

        await _context.SaveChangesAsync();
    }

    private class StatementExportRow
    {
        public string Sequence { get; set; } = string.Empty;
        public DateTime? EntryDate { get; set; }
        public string? Description { get; set; }
        public string? SiteName { get; set; }
        public string? Category { get; set; }
        public DateTime? ApprovalDate { get; set; }
        public decimal? DebitedAmount { get; set; }
        public decimal? CreditedAmount { get; set; }
        public decimal RemainingAmount { get; set; }
        public bool IsOpeningOrClosingRow { get; set; }
    }

    private class StatementExportEvent
    {
        public DateTime SortDate { get; set; }
        public DateTime? EntryDate { get; set; }
        public string? Description { get; set; }
        public string? SiteName { get; set; }
        public string? Category { get; set; }
        public DateTime? ApprovalDate { get; set; }
        public BalanceTransactionType Type { get; set; }
        public decimal Amount { get; set; }
    }

    /// <summary>
    /// Maps a Voucher entity to a response DTO.
    /// S3 object keys stored in <c>ReceiptUrls</c> are converted to
    /// 1-hour pre-signed HTTPS URLs at response-time.
    /// </summary>
    private VoucherResponse MapToResponse(Voucher voucher)
    {
        var assignedAdminName = ResolveAssignedAdminName(voucher);
        var resolvedTargetPillerName = ResolveTargetPillerName(voucher);
        var resolvedOwningSuperAdminName = ResolveOwningSuperAdminName(voucher);

        return new VoucherResponse
        {
            Id = voucher.Id,
            VoucherNumber = voucher.VoucherNumber,
            Title = voucher.Title,
            Description = voucher.Description,
            Amount = voucher.Amount,
            VoucherDate = voucher.VoucherDate,
            Category = ResolveCategoryName(voucher.Category),
            CategoryName = ResolveCategoryName(voucher.Category),
            SiteName = voucher.SiteName,
            Status = voucher.Status,
            StatusName = voucher.Status switch
            {
                VoucherStatus.PendingPiller => "Pending Piller",
                VoucherStatus.ApprovedPiller => "Approved Piller",
                VoucherStatus.RejectedPiller => "Rejected Piller",
                VoucherStatus.PendingAdmin => "Pending Admin",
                VoucherStatus.ApprovedAdmin => "Approved Admin",
                VoucherStatus.RejectedAdmin => "Rejected Admin",
                VoucherStatus.PendingSuperAdmin => "Pending SuperAdmin",
                VoucherStatus.Approved => "Approved",
                VoucherStatus.Rejected => "Rejected",
                _ => "Unknown",
            },
            CurrentApprovalLevel = voucher.CurrentApprovalLevel,
            // Convert each stored S3 key to a pre-signed URL
            ReceiptUrls = (voucher.ReceiptUrls ?? new()).Select(k => _storage.GetPresignedUrl(k)).ToList(),
            TargetPillerId = voucher.TargetPillerId,
            TargetPillerName = resolvedTargetPillerName,
            AssignedAdminName = assignedAdminName,
            OwningSuperAdminId = voucher.OwningSuperAdminId,
            OwningSuperAdminName = resolvedOwningSuperAdminName,
            CreatedById = voucher.CreatedById,
            CreatedByName = voucher.CreatedBy != null
                ? $"{voucher.CreatedBy.FirstName} {voucher.CreatedBy.LastName}"
                : string.Empty,
            CreatedAt = voucher.CreatedAt,
            UpdatedAt = voucher.UpdatedAt,
            AdminApprovedById = voucher.AdminApprovedById,
            AdminApprovedByName = voucher.AdminApprovedBy != null
                ? $"{voucher.AdminApprovedBy.FirstName} {voucher.AdminApprovedBy.LastName}"
                : null,
            AdminApprovedAt = voucher.AdminApprovedAt,
            SuperAdminApprovedById = voucher.SuperAdminApprovedById,
            SuperAdminApprovedByName = voucher.SuperAdminApprovedBy != null
                ? $"{voucher.SuperAdminApprovedBy.FirstName} {voucher.SuperAdminApprovedBy.LastName}"
                : null,
            SuperAdminApprovedAt = voucher.SuperAdminApprovedAt,
            PillerApprovedById = voucher.PillerApprovedById,
            PillerApprovedByName = voucher.PillerApprovedBy != null
                ? $"{voucher.PillerApprovedBy.FirstName} {voucher.PillerApprovedBy.LastName}"
                : null,
            PillerApprovedAt = voucher.PillerApprovedAt,
            RejectedById = voucher.RejectedById,
            RejectedByName = voucher.RejectedBy != null
                ? $"{voucher.RejectedBy.FirstName} {voucher.RejectedBy.LastName}"
                : null,
            RejectedAt = voucher.RejectedAt,
            RejectionReason = voucher.RejectionReason,
            ReopenDeadlineAt = voucher.Status == VoucherStatus.Rejected && voucher.RejectedAt.HasValue
                ? voucher.RejectedAt.Value.AddDays(7)
                : null,
        };
    }

    private string? ResolveAssignedAdminName(Voucher voucher)
    {
        if (voucher.AdminApprovedBy != null)
        {
            return $"{voucher.AdminApprovedBy.FirstName} {voucher.AdminApprovedBy.LastName}";
        }

        if (!voucher.OwningSuperAdminId.HasValue)
        {
            return null;
        }

        var voucherManagementSlug = PermissionSlugs.VoucherManagement;

        var adminCandidates = _context.Users
            .AsNoTracking()
            .Include(u => u.AdminRole)
            .ThenInclude(r => r!.RolePermissions)
            .ThenInclude(rp => rp.Permission)
            .Where(u =>
                u.Role == UserRole.Admin &&
                u.SuperAdminId == voucher.OwningSuperAdminId &&
                u.AdminRole != null &&
                u.AdminRole.RolePermissions.Any(rp => rp.Permission.Slug == voucherManagementSlug))
            .ToList();

        var preferred = adminCandidates.FirstOrDefault(u =>
            u.AdminRole != null &&
            string.Equals(u.AdminRole.Name.Trim(), "account-admin", StringComparison.OrdinalIgnoreCase));

        var selected = preferred ?? adminCandidates.FirstOrDefault();
        if (selected == null)
        {
            return null;
        }

        return $"{selected.FirstName} {selected.LastName}";
    }

    private string? ResolveTargetPillerName(Voucher voucher)
    {
        if (voucher.TargetPiller != null)
        {
            return $"{voucher.TargetPiller.FirstName} {voucher.TargetPiller.LastName}";
        }

        if (!voucher.TargetPillerId.HasValue)
        {
            return null;
        }

        var piller = _context.Users
            .AsNoTracking()
            .FirstOrDefault(u => u.Id == voucher.TargetPillerId.Value);

        return piller != null ? $"{piller.FirstName} {piller.LastName}" : null;
    }

    private string? ResolveOwningSuperAdminName(Voucher voucher)
    {
        if (voucher.OwningSuperAdmin != null)
        {
            return $"{voucher.OwningSuperAdmin.FirstName} {voucher.OwningSuperAdmin.LastName}";
        }

        if (!voucher.OwningSuperAdminId.HasValue)
        {
            return null;
        }

        var superAdmin = _context.Users
            .AsNoTracking()
            .FirstOrDefault(u => u.Id == voucher.OwningSuperAdminId.Value);

        return superAdmin != null ? $"{superAdmin.FirstName} {superAdmin.LastName}" : null;
    }

    private static bool IsAccountsAdmin(User user)
        => user.AdminRole != null
           && string.Equals(user.AdminRole.Name.Trim(), "account-admin", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Converts any legacy numeric or PascalCase enum string stored in the DB
    /// to a proper human-readable category label.
    /// </summary>
    private static string ResolveCategoryName(string raw) =>
        raw switch
        {
            "0" or "FuelExpense"       => "Fuel Expense",
            "1" or "LabourExpense"     => "Labour Expense",
            "2" or "OfficeExpense"     => "Office Expense",
            "3" or "OverheadExpense"   => "Overhead Expense",
            "4" or "VehicleExpense"    => "Vehicle Expense",
            "5" or "Transportation"    => "Transportation",
            "6" or "OtherExpense"      => "Other Expense",
            _                          => raw,
        };
}
