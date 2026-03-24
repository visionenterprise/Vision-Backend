using Microsoft.EntityFrameworkCore;
using vision_backend.Application.Constants;
using vision_backend.Application.DTOs.Leaves;
using vision_backend.Application.DTOs.Users;
using vision_backend.Application.Interfaces;
using vision_backend.Domain.Entities;
using vision_backend.Domain.Enums;
using vision_backend.Infrastructure.Data;
using vision_backend.Infrastructure.Repositories;

namespace vision_backend.Application.Services;

public class LeaveService : ILeaveService
{
    private static readonly HashSet<LeaveStatus> NonActiveStatuses =
    [
        LeaveStatus.Rejected,
        LeaveStatus.RejectedAdmin,
        LeaveStatus.RejectedPiller,
    ];

    private readonly ILeaveRequestRepository _leaveRepository;
    private readonly IUserRepository _userRepository;
    private readonly IUserService _userService;
    private readonly INotificationService _notificationService;
    private readonly ApplicationDbContext _context;

    public LeaveService(
        ILeaveRequestRepository leaveRepository,
        IUserRepository userRepository,
        IUserService userService,
        INotificationService notificationService,
        ApplicationDbContext context)
    {
        _leaveRepository = leaveRepository;
        _userRepository = userRepository;
        _userService = userService;
        _notificationService = notificationService;
        _context = context;
    }

    public async Task<LeaveResponse> ApplyForLeaveAsync(Guid userId, ApplyLeaveRequest request)
    {
        var user = await _userRepository.GetByIdAsync(userId)
            ?? throw new InvalidOperationException("User not found.");
        var requesterRole = EffectiveRoleResolver.GetEffectiveRole(user);

        if (request.StartDate > request.EndDate)
            throw new InvalidOperationException("Start date cannot be after end date.");

        if (request.StartDate.Year != request.EndDate.Year)
            throw new InvalidOperationException("Leave request must be within the same calendar year.");

        if (requesterRole == UserRole.GeneralUser && request.TargetPillerId == null)
            throw new InvalidOperationException("A target Piller must be selected for general user leave requests.");

        var leaveType = await _context.LeaveTypes
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == request.LeaveTypeId && t.IsActive)
            ?? throw new InvalidOperationException("Selected leave type not found or inactive.");

        var assignment = await _context.LeaveTypeAssignments
            .Include(a => a.LeaveType)
            .AsNoTracking()
            .FirstOrDefaultAsync(a =>
                a.UserId == userId
                && a.LeaveTypeId == request.LeaveTypeId
                && a.LeaveType.IsActive);

        if (assignment == null)
            throw new InvalidOperationException("This leave type is not assigned to the selected employee.");

        Guid? targetPillerId = request.TargetPillerId;
        Guid? owningSuperAdminId = null;

        if (requesterRole == UserRole.GeneralUser)
        {
            var piller = await _userRepository.GetByIdAsync(request.TargetPillerId!.Value)
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

        var hasDateOverlap = await _context.LeaveRequests
            .AnyAsync(l =>
                l.UserId == userId
                && !NonActiveStatuses.Contains(l.Status)
                && request.StartDate <= l.EndDate
                && request.EndDate >= l.StartDate);

        if (hasDateOverlap)
            throw new InvalidOperationException("You already have an active leave request overlapping these dates.");

        var (requestedDays, holidayOverlapDays, deductedDays) = await CalculateLeaveDayMetricsAsync(request.StartDate, request.EndDate);

        if (deductedDays <= 0)
            throw new InvalidOperationException("Selected leave dates overlap only non-working days (holidays/Sundays). No leave can be deducted.");

        var leaveYear = request.StartDate.Year;
        await ValidateQuotaAsync(userId, leaveType, assignment, leaveYear, deductedDays, includePending: true);

        var initialApprovalLevel = requesterRole == UserRole.SuperAdmin
            ? LeaveApprovalLevel.SuperAdmin
            : requesterRole == UserRole.Admin
                ? LeaveApprovalLevel.SuperAdmin
                : requesterRole == UserRole.Piller
                    ? LeaveApprovalLevel.Admin
                    : LeaveApprovalLevel.Piller;

        var initialStatus = requesterRole == UserRole.SuperAdmin
            ? LeaveStatus.Approved
            : requesterRole == UserRole.Admin
                ? LeaveStatus.PendingSuperAdmin
                : requesterRole == UserRole.Piller
                    ? LeaveStatus.PendingAdmin
                    : LeaveStatus.PendingPiller;

        var now = DateTime.Now;
        var leaveRequest = new LeaveRequest
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TargetPillerId = targetPillerId,
            OwningSuperAdminId = owningSuperAdminId,
            LeaveTypeId = leaveType.Id,
            LeaveTypeName = leaveType.Name,
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            RequestedDays = requestedDays,
            HolidayOverlapDays = holidayOverlapDays,
            DeductedDays = deductedDays,
            LeaveYear = leaveYear,
            Reason = request.Reason,
            Status = initialStatus,
            CurrentApprovalLevel = initialApprovalLevel,
            CreatedAt = now,
            UpdatedAt = now,
        };

        if (requesterRole == UserRole.SuperAdmin)
            leaveRequest.SuperAdminApprovedById = userId;

        await _leaveRepository.AddAsync(leaveRequest);

        var created = await _leaveRepository.GetByIdAsync(leaveRequest.Id)
            ?? throw new InvalidOperationException("Failed to retrieve created leave request.");

        await _notificationService.NotifyLeaveApprovalPendingAsync(
            leaveRequest.Id,
            leaveRequest.UserId,
            leaveRequest.CurrentApprovalLevel,
            leaveRequest.TargetPillerId,
            leaveRequest.OwningSuperAdminId);

        return MapToResponse(created);
    }

    public async Task<LeaveResponse> ApproveLeaveAsync(Guid leaveId, Guid approverId, UserRole approverRole)
    {
        var approver = await _userRepository.GetByIdAsync(approverId)
            ?? throw new InvalidOperationException("Approver user not found.");
        var effectiveApproverRole = EffectiveRoleResolver.GetEffectiveRole(approver);

        var leave = await _leaveRepository.GetByIdAsync(leaveId)
            ?? throw new InvalidOperationException("Leave request not found.");

        if (leave.Status == LeaveStatus.Approved)
            throw new InvalidOperationException("Leave is already fully approved.");

        if (NonActiveStatuses.Contains(leave.Status))
            throw new InvalidOperationException("Cannot approve a rejected leave.");

        var now = DateTime.Now;

        if (effectiveApproverRole == UserRole.Piller)
        {
            if (leave.CurrentApprovalLevel != LeaveApprovalLevel.Piller)
                throw new InvalidOperationException("Leave is not waiting for Piller approval.");

            if (leave.TargetPillerId != approverId)
                throw new InvalidOperationException("This leave is assigned to a different piller.");

            if (!await _userService.HasPermissionAsync(approverId, PermissionSlugs.LeaveApprovals))
                throw new InvalidOperationException("Piller does not have leave_approvals permission.");

            leave.PillerApprovedById = approverId;
            leave.CurrentApprovalLevel = LeaveApprovalLevel.Admin;
            leave.Status = LeaveStatus.PendingAdmin;
        }
        else if (effectiveApproverRole == UserRole.Admin)
        {
            if (leave.CurrentApprovalLevel != LeaveApprovalLevel.Admin)
                throw new InvalidOperationException("Leave is not waiting for HR-Admin approval.");

            if (!await _userService.HasPermissionAsync(approverId, PermissionSlugs.LeaveApprovals))
                throw new InvalidOperationException("Admin does not have leave_approvals permission.");

            leave.AdminApprovedById = approverId;
            leave.CurrentApprovalLevel = LeaveApprovalLevel.SuperAdmin;
            leave.Status = LeaveStatus.PendingSuperAdmin;
        }
        else if (effectiveApproverRole == UserRole.SuperAdmin)
        {
            if (leave.CurrentApprovalLevel != LeaveApprovalLevel.SuperAdmin)
                throw new InvalidOperationException("Leave is not waiting for Super Admin approval.");

            var assignment = await _context.LeaveTypeAssignments
                .Include(a => a.LeaveType)
                .AsNoTracking()
                .FirstOrDefaultAsync(a => a.UserId == leave.UserId && a.LeaveTypeId == leave.LeaveTypeId);

            if (assignment == null || assignment.LeaveType == null || !assignment.LeaveType.IsActive)
                throw new InvalidOperationException("Assigned leave type is missing or inactive for this request.");

            var (requestedDays, holidayOverlapDays, deductedDays) = await CalculateLeaveDayMetricsAsync(leave.StartDate, leave.EndDate);
            leave.RequestedDays = requestedDays;
            leave.HolidayOverlapDays = holidayOverlapDays;
            leave.DeductedDays = deductedDays;

            await ValidateQuotaAsync(
                leave.UserId,
                assignment.LeaveType,
                assignment,
                leave.LeaveYear,
                deductedDays,
                includePending: false,
                excludeLeaveRequestId: leave.Id);

            leave.SuperAdminApprovedById = approverId;
            leave.Status = LeaveStatus.Approved;
        }
        else
        {
            throw new InvalidOperationException("Invalid role for approval.");
        }

        leave.UpdatedAt = now;
        await _leaveRepository.UpdateAsync(leave);

        var updated = await _leaveRepository.GetByIdAsync(leaveId)
            ?? throw new InvalidOperationException("Failed to retrieve updated leave.");

        await _notificationService.NotifyLeaveDecisionAsync(updated.Id, updated.UserId, updated.Status);

        if (updated.Status != LeaveStatus.Approved)
        {
            await _notificationService.NotifyLeaveApprovalPendingAsync(
                updated.Id,
                updated.UserId,
                updated.CurrentApprovalLevel,
                updated.TargetPillerId,
                updated.OwningSuperAdminId);
        }

        return MapToResponse(updated);
    }

    public async Task<LeaveResponse> RejectLeaveAsync(Guid leaveId, Guid rejectorId, UserRole rejectorRole, string reason)
    {
        var rejector = await _userRepository.GetByIdAsync(rejectorId)
            ?? throw new InvalidOperationException("Rejector user not found.");
        var effectiveRejectorRole = EffectiveRoleResolver.GetEffectiveRole(rejector);

        var leave = await _leaveRepository.GetByIdAsync(leaveId)
            ?? throw new InvalidOperationException("Leave request not found.");

        if (leave.Status == LeaveStatus.Approved)
            throw new InvalidOperationException("Cannot reject an already approved leave.");

        if (NonActiveStatuses.Contains(leave.Status))
            throw new InvalidOperationException("Leave is already rejected.");

        var canReject = false;
        if (effectiveRejectorRole == UserRole.Piller && leave.CurrentApprovalLevel == LeaveApprovalLevel.Piller) canReject = true;
        if (effectiveRejectorRole == UserRole.Admin && (leave.CurrentApprovalLevel == LeaveApprovalLevel.Piller || leave.CurrentApprovalLevel == LeaveApprovalLevel.Admin)) canReject = true;
        if (effectiveRejectorRole == UserRole.SuperAdmin) canReject = true;

        if (!canReject)
            throw new InvalidOperationException("You do not have permission to reject this leave at its current stage.");

        if (effectiveRejectorRole == UserRole.Piller && leave.TargetPillerId != rejectorId)
            throw new InvalidOperationException("This leave is assigned to a different piller.");

        if (effectiveRejectorRole == UserRole.Piller && !await _userService.HasPermissionAsync(rejectorId, PermissionSlugs.LeaveApprovals))
            throw new InvalidOperationException("Piller does not have leave_approvals permission.");

        if (effectiveRejectorRole == UserRole.Admin && !await _userService.HasPermissionAsync(rejectorId, PermissionSlugs.LeaveApprovals))
            throw new InvalidOperationException("Admin does not have leave_approvals permission.");

        leave.Status = effectiveRejectorRole switch
        {
            UserRole.Piller => LeaveStatus.RejectedPiller,
            UserRole.Admin => LeaveStatus.RejectedAdmin,
            _ => LeaveStatus.Rejected,
        };

        leave.RejectedById = rejectorId;
        leave.RejectionReason = reason;
        leave.UpdatedAt = DateTime.Now;

        await _leaveRepository.UpdateAsync(leave);

        var updated = await _leaveRepository.GetByIdAsync(leaveId)
            ?? throw new InvalidOperationException("Failed to retrieve updated leave.");

        await _notificationService.NotifyLeaveDecisionAsync(updated.Id, updated.UserId, updated.Status);

        return MapToResponse(updated);
    }

    public async Task<IEnumerable<LeaveResponse>> GetMyLeavesAsync(Guid userId)
    {
        var leaves = await _leaveRepository.GetByUserIdAsync(userId);
        var responses = new List<LeaveResponse>();
        foreach (var leave in leaves)
        {
            responses.Add(await MapToResponseWithRecalculatedMetricsAsync(leave));
        }

        return responses;
    }

    public async Task<IEnumerable<LeaveResponse>> GetPendingApprovalsAsync(Guid approverId, UserRole approverRole)
    {
        var approver = await _userRepository.GetByIdAsync(approverId)
            ?? throw new InvalidOperationException("Approver user not found.");
        var effectiveApproverRole = EffectiveRoleResolver.GetEffectiveRole(approver);

        LeaveApprovalLevel expectedLevel;
        Guid? targetPillerId = null;
        Guid? owningSuperAdminId = null;
        var includeDirectSelfRequestsForPiller = false;
        var includeApprovalHistory = true;
        var adminSharedApprovalAccess = false;

        if (effectiveApproverRole == UserRole.Piller)
        {
            if (!await _userService.HasPermissionAsync(approverId, PermissionSlugs.LeaveApprovals))
                throw new InvalidOperationException("Piller does not have leave_approvals permission.");

            expectedLevel = LeaveApprovalLevel.Piller;
            targetPillerId = approverId;
            includeDirectSelfRequestsForPiller = true;
        }
        else if (effectiveApproverRole == UserRole.Admin)
        {
            if (!await _userService.HasPermissionAsync(approverId, PermissionSlugs.LeaveApprovals))
                throw new InvalidOperationException("Admin does not have leave_approvals permission.");

            expectedLevel = LeaveApprovalLevel.Admin;
            adminSharedApprovalAccess = true;
        }
        else if (effectiveApproverRole == UserRole.SuperAdmin)
        {
            expectedLevel = LeaveApprovalLevel.SuperAdmin;
            owningSuperAdminId = null;
        }
        else
        {
            throw new InvalidOperationException("Role cannot approve leaves.");
        }

        var leaves = await _leaveRepository.GetPendingByApprovalLevelAsync(
            expectedLevel,
            targetPillerId,
            owningSuperAdminId,
            includeDirectSelfRequestsForPiller,
            includeApprovalHistory,
            adminSharedApprovalAccess);

        var responses = new List<LeaveResponse>();
        foreach (var leave in leaves)
        {
            responses.Add(await MapToResponseWithRecalculatedMetricsAsync(leave));
        }

        return responses;
    }

    public async Task<IEnumerable<LeaveResponse>> GetUpcomingLeavesAsync()
    {
        var leaves = await _leaveRepository.GetUpcomingApprovedLeavesAsync(DateTime.Now);
        var responses = new List<LeaveResponse>();
        foreach (var leave in leaves)
        {
            responses.Add(await MapToResponseWithRecalculatedMetricsAsync(leave));
        }

        return responses;
    }
    private async Task<(int RequestedDays, int HolidayOverlapDays, int DeductedDays)> CalculateLeaveDayMetricsAsync(DateOnly startDate, DateOnly endDate)
    {
        var totalSelectedDays = CalculateInclusiveDays(startDate, endDate);
        var holidayDates = await GetHolidayDatesAsync(startDate, endDate);
        var sundayOverlapDays = CountSundaysInRange(startDate, endDate);
        var requestedDays = Math.Max(0, totalSelectedDays - sundayOverlapDays);
        var holidayOverlapDays = CountHolidayOverlapExcludingSundays(holidayDates);
        var deductedDays = Math.Max(0, requestedDays - holidayOverlapDays);

        return (requestedDays, holidayOverlapDays, deductedDays);
    }


    public async Task<IEnumerable<LeaveTypeResponse>> GetLeaveTypesAsync(bool includeInactive = false)
    {
        var query = _context.LeaveTypes.AsNoTracking().AsQueryable();
        if (!includeInactive)
            query = query.Where(t => t.IsActive);

        var leaveTypes = await query
            .OrderBy(t => t.Name)
            .Select(t => new LeaveTypeResponse
            {
                Id = t.Id,
                Name = t.Name,
                Description = t.Description,
                IsPaid = t.IsPaid,
                IsActive = t.IsActive,
                AnnualQuotaDays = t.AnnualQuotaDays,
                CreatedAt = t.CreatedAt,
                UpdatedAt = t.UpdatedAt,
                AssignedEmployeesCount = t.Assignments.Count,
            })
            .ToListAsync();

        return leaveTypes;
    }

    public async Task<LeaveTypeResponse> CreateLeaveTypeAsync(Guid actorId, CreateLeaveTypeRequest request)
    {
        await EnsureCanManageLeaveMasterDataAsync(actorId);

        var normalizedName = request.Name.Trim();
        if (string.IsNullOrWhiteSpace(normalizedName))
            throw new InvalidOperationException("Leave type name is required.");

        var duplicate = await _context.LeaveTypes
            .AnyAsync(t => t.Name.ToLower() == normalizedName.ToLower());

        if (duplicate)
            throw new InvalidOperationException("A leave type with this name already exists.");

        var now = DateTime.Now;
        var leaveType = new LeaveType
        {
            Id = Guid.NewGuid(),
            Name = normalizedName,
            Description = request.Description?.Trim(),
            IsPaid = request.IsPaid,
            IsActive = request.IsActive,
            AnnualQuotaDays = request.AnnualQuotaDays,
            CreatedAt = now,
            UpdatedAt = now,
        };

        _context.LeaveTypes.Add(leaveType);
        await _context.SaveChangesAsync();

        await EnsureAssignmentsForLeaveTypeAsync(leaveType.Id, now);

        var assignedEmployeesCount = await _context.LeaveTypeAssignments
            .AsNoTracking()
            .CountAsync(a => a.LeaveTypeId == leaveType.Id);

        return MapToLeaveTypeResponse(leaveType, assignedEmployeesCount);
    }

    public async Task<LeaveTypeResponse> UpdateLeaveTypeAsync(Guid actorId, Guid leaveTypeId, UpdateLeaveTypeRequest request)
    {
        await EnsureCanManageLeaveMasterDataAsync(actorId);

        var leaveType = await _context.LeaveTypes
            .Include(t => t.Assignments)
            .FirstOrDefaultAsync(t => t.Id == leaveTypeId)
            ?? throw new InvalidOperationException("Leave type not found.");

        var normalizedName = request.Name.Trim();
        if (string.IsNullOrWhiteSpace(normalizedName))
            throw new InvalidOperationException("Leave type name is required.");

        var duplicate = await _context.LeaveTypes
            .AnyAsync(t => t.Id != leaveTypeId && t.Name.ToLower() == normalizedName.ToLower());

        if (duplicate)
            throw new InvalidOperationException("A leave type with this name already exists.");

        leaveType.Name = normalizedName;
        leaveType.Description = request.Description?.Trim();
        leaveType.IsPaid = request.IsPaid;
        leaveType.IsActive = request.IsActive;
        leaveType.AnnualQuotaDays = request.AnnualQuotaDays;
        leaveType.UpdatedAt = DateTime.Now;

        await _context.SaveChangesAsync();

        return MapToLeaveTypeResponse(leaveType, leaveType.Assignments.Count);
    }

    public async Task DeleteLeaveTypeAsync(Guid actorId, Guid leaveTypeId)
    {
        await EnsureCanManageLeaveMasterDataAsync(actorId);

        var leaveType = await _context.LeaveTypes
            .Include(t => t.Assignments)
            .FirstOrDefaultAsync(t => t.Id == leaveTypeId)
            ?? throw new InvalidOperationException("Leave type not found.");

        var hasLeaveHistory = await _context.LeaveRequests.AnyAsync(l => l.LeaveTypeId == leaveTypeId);
        if (hasLeaveHistory)
            throw new InvalidOperationException("Cannot delete a leave type that already has leave requests. Mark it inactive instead.");

        _context.LeaveTypeAssignments.RemoveRange(leaveType.Assignments);
        _context.LeaveTypes.Remove(leaveType);
        await _context.SaveChangesAsync();
    }

    public async Task<LeaveTypeAssignmentResponse> AssignLeaveTypeAsync(Guid actorId, Guid leaveTypeId, AssignLeaveTypeRequest request)
    {
        await EnsureCanManageLeaveMasterDataAsync(actorId);

        var leaveType = await _context.LeaveTypes
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == leaveTypeId)
            ?? throw new InvalidOperationException("Leave type not found.");

        if (!leaveType.IsActive)
            throw new InvalidOperationException("Cannot assign an inactive leave type.");

        var user = await _userRepository.GetByIdAsync(request.UserId)
            ?? throw new InvalidOperationException("User not found.");

        var existing = await _context.LeaveTypeAssignments
            .Include(a => a.LeaveType)
            .Include(a => a.User)
                .ThenInclude(u => u.AdminRole)
            .FirstOrDefaultAsync(a => a.LeaveTypeId == leaveTypeId && a.UserId == request.UserId);

        var now = DateTime.Now;

        if (existing != null)
        {
            existing.AnnualQuotaDaysOverride = request.AnnualQuotaDaysOverride;
            existing.UpdatedAt = now;
            await _context.SaveChangesAsync();
            return await MapToLeaveTypeAssignmentResponseAsync(existing, existing.LeaveType.AnnualQuotaDays);
        }

        var assignment = new LeaveTypeAssignment
        {
            Id = Guid.NewGuid(),
            LeaveTypeId = leaveTypeId,
            UserId = user.Id,
            AnnualQuotaDaysOverride = request.AnnualQuotaDaysOverride,
            CreatedAt = now,
            UpdatedAt = now,
        };

        _context.LeaveTypeAssignments.Add(assignment);
        await _context.SaveChangesAsync();

        var created = await _context.LeaveTypeAssignments
            .Include(a => a.LeaveType)
            .Include(a => a.User)
                .ThenInclude(u => u.AdminRole)
            .FirstAsync(a => a.Id == assignment.Id);

        return await MapToLeaveTypeAssignmentResponseAsync(created, created.LeaveType.AnnualQuotaDays);
    }

    public async Task<AssignLeaveTypeBatchResponse> AssignLeaveTypeBatchAsync(Guid actorId, Guid leaveTypeId, AssignLeaveTypeBatchRequest request)
    {
        await EnsureCanManageLeaveMasterDataAsync(actorId);

        var leaveType = await _context.LeaveTypes
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == leaveTypeId)
            ?? throw new InvalidOperationException("Leave type not found.");

        if (!leaveType.IsActive)
            throw new InvalidOperationException("Cannot assign an inactive leave type.");

        var distinctUserIds = request.UserIds
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();

        if (distinctUserIds.Count == 0)
            throw new InvalidOperationException("Please select at least one valid user for batch assignment.");

        var eligibleUserIds = await _context.Users
            .AsNoTracking()
            .Where(u => distinctUserIds.Contains(u.Id) && u.Role != UserRole.SuperAdmin)
            .Select(u => u.Id)
            .ToListAsync();

        var existingAssignments = await _context.LeaveTypeAssignments
            .Where(a => a.LeaveTypeId == leaveTypeId && eligibleUserIds.Contains(a.UserId))
            .ToListAsync();

        var existingByUserId = existingAssignments.ToDictionary(a => a.UserId, a => a);
        var now = DateTime.Now;

        var assignedCount = 0;
        var updatedCount = 0;
        var skippedCount = 0;

        foreach (var userId in eligibleUserIds)
        {
            if (existingByUserId.TryGetValue(userId, out var existing))
            {
                if (!request.OverwriteExistingAssignments)
                {
                    skippedCount++;
                    continue;
                }

                existing.AnnualQuotaDaysOverride = request.AnnualQuotaDaysOverride;
                existing.UpdatedAt = now;
                updatedCount++;
                continue;
            }

            _context.LeaveTypeAssignments.Add(new LeaveTypeAssignment
            {
                Id = Guid.NewGuid(),
                LeaveTypeId = leaveTypeId,
                UserId = userId,
                AnnualQuotaDaysOverride = request.AnnualQuotaDaysOverride,
                CreatedAt = now,
                UpdatedAt = now,
            });

            assignedCount++;
        }

        await _context.SaveChangesAsync();

        return new AssignLeaveTypeBatchResponse
        {
            RequestedCount = distinctUserIds.Count,
            EligibleCount = eligibleUserIds.Count,
            AssignedCount = assignedCount,
            UpdatedCount = updatedCount,
            SkippedCount = skippedCount,
            IneligibleCount = distinctUserIds.Count - eligibleUserIds.Count,
        };
    }

    public async Task<AssignLeaveTypeToAllResponse> AssignLeaveTypeToAllUsersAsync(Guid actorId, Guid leaveTypeId, AssignLeaveTypeToAllRequest request)
    {
        await EnsureCanManageLeaveMasterDataAsync(actorId);

        var leaveType = await _context.LeaveTypes
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == leaveTypeId)
            ?? throw new InvalidOperationException("Leave type not found.");

        if (!leaveType.IsActive)
            throw new InvalidOperationException("Cannot assign an inactive leave type.");

        var eligibleUsers = await _context.Users
            .AsNoTracking()
            .Where(u => u.Role != UserRole.SuperAdmin)
            .Select(u => u.Id)
            .ToListAsync();

        var existingAssignments = await _context.LeaveTypeAssignments
            .Where(a => a.LeaveTypeId == leaveTypeId)
            .ToListAsync();

        var existingByUserId = existingAssignments.ToDictionary(a => a.UserId, a => a);
        var now = DateTime.Now;

        var assignedCount = 0;
        var updatedCount = 0;
        var skippedCount = 0;

        foreach (var userId in eligibleUsers)
        {
            if (existingByUserId.TryGetValue(userId, out var existing))
            {
                if (!request.OverwriteExistingAssignments)
                {
                    skippedCount++;
                    continue;
                }

                existing.AnnualQuotaDaysOverride = request.AnnualQuotaDaysOverride;
                existing.UpdatedAt = now;
                updatedCount++;
                continue;
            }

            _context.LeaveTypeAssignments.Add(new LeaveTypeAssignment
            {
                Id = Guid.NewGuid(),
                LeaveTypeId = leaveTypeId,
                UserId = userId,
                AnnualQuotaDaysOverride = request.AnnualQuotaDaysOverride,
                CreatedAt = now,
                UpdatedAt = now,
            });

            assignedCount++;
        }

        await _context.SaveChangesAsync();

        return new AssignLeaveTypeToAllResponse
        {
            TotalEligibleUsers = eligibleUsers.Count,
            AssignedCount = assignedCount,
            UpdatedCount = updatedCount,
            SkippedCount = skippedCount,
        };
    }

    public async Task<LeaveTypeAssignmentResponse> UpdateLeaveTypeAssignmentAsync(Guid actorId, Guid assignmentId, AssignLeaveTypeRequest request)
    {
        await EnsureCanManageLeaveMasterDataAsync(actorId);

        var assignment = await _context.LeaveTypeAssignments
            .Include(a => a.LeaveType)
            .Include(a => a.User)
                .ThenInclude(u => u.AdminRole)
            .FirstOrDefaultAsync(a => a.Id == assignmentId)
            ?? throw new InvalidOperationException("Leave assignment not found.");

        assignment.AnnualQuotaDaysOverride = request.AnnualQuotaDaysOverride;
        assignment.UpdatedAt = DateTime.Now;

        await _context.SaveChangesAsync();

        return await MapToLeaveTypeAssignmentResponseAsync(assignment, assignment.LeaveType.AnnualQuotaDays);
    }

    public async Task DeleteLeaveTypeAssignmentAsync(Guid actorId, Guid assignmentId)
    {
        await EnsureCanManageLeaveMasterDataAsync(actorId);

        var assignment = await _context.LeaveTypeAssignments
            .FirstOrDefaultAsync(a => a.Id == assignmentId)
            ?? throw new InvalidOperationException("Leave assignment not found.");

        var hasLeaveHistory = await _context.LeaveRequests.AnyAsync(l =>
            l.UserId == assignment.UserId
            && l.LeaveTypeId == assignment.LeaveTypeId
            && !NonActiveStatuses.Contains(l.Status));

        if (hasLeaveHistory)
            throw new InvalidOperationException("Cannot remove assignment while active leave requests exist for this leave type.");

        _context.LeaveTypeAssignments.Remove(assignment);
        await _context.SaveChangesAsync();
    }

    public async Task<IEnumerable<LeaveTypeAssignmentResponse>> GetLeaveTypeAssignmentsAsync(Guid leaveTypeId)
    {
        var leaveType = await _context.LeaveTypes
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == leaveTypeId)
            ?? throw new InvalidOperationException("Leave type not found.");

        await EnsureAssignmentsForLeaveTypeAsync(leaveTypeId);

        var assignments = await _context.LeaveTypeAssignments
            .AsNoTracking()
            .Include(a => a.LeaveType)
            .Include(a => a.User)
                .ThenInclude(u => u.AdminRole)
            .Where(a => a.LeaveTypeId == leaveTypeId)
            .OrderBy(a => a.User.FirstName)
            .ThenBy(a => a.User.LastName)
            .ToListAsync();

        var responses = new List<LeaveTypeAssignmentResponse>(assignments.Count);
        foreach (var assignment in assignments)
        {
            responses.Add(await MapToLeaveTypeAssignmentResponseAsync(assignment, leaveType.AnnualQuotaDays));
        }

        return responses;
    }

    public async Task<IEnumerable<LeaveTypeResponse>> GetMyAssignableLeaveTypesAsync(Guid userId)
    {
        await EnsureAssignmentsForUserAsync(userId);

        var leaveTypes = await _context.LeaveTypeAssignments
            .AsNoTracking()
            .Include(a => a.LeaveType)
            .Where(a => a.UserId == userId && a.LeaveType.IsActive)
            .Select(a => new LeaveTypeResponse
            {
                Id = a.LeaveTypeId,
                Name = a.LeaveType.Name,
                Description = a.LeaveType.Description,
                IsPaid = a.LeaveType.IsPaid,
                IsActive = a.LeaveType.IsActive,
                AnnualQuotaDays = a.AnnualQuotaDaysOverride ?? a.LeaveType.AnnualQuotaDays,
                CreatedAt = a.LeaveType.CreatedAt,
                UpdatedAt = a.LeaveType.UpdatedAt,
                AssignedEmployeesCount = 0,
            })
            .OrderBy(t => t.Name)
            .ToListAsync();

        return leaveTypes;
    }

    public async Task<IEnumerable<LeaveBalanceResponse>> GetMyLeaveBalancesAsync(Guid userId, int? year = null)
    {
        await EnsureAssignmentsForUserAsync(userId);

        var targetYear = year ?? DateTime.Now.Year;

        var assignments = await _context.LeaveTypeAssignments
            .AsNoTracking()
            .Include(a => a.LeaveType)
            .Where(a => a.UserId == userId && a.LeaveType.IsActive)
            .OrderBy(a => a.LeaveType.Name)
            .ToListAsync();

        var balances = new List<LeaveBalanceResponse>();
        foreach (var assignment in assignments)
        {
            var annualQuota = assignment.AnnualQuotaDaysOverride ?? assignment.LeaveType.AnnualQuotaDays;
            var used = await GetUsedLeaveDaysAsync(
                userId,
                assignment.LeaveTypeId,
                assignment.LeaveType.Name,
                targetYear,
                includePending: false);
            var remaining = annualQuota.HasValue
                ? Math.Max(0, annualQuota.Value - used)
                : 0;

            balances.Add(new LeaveBalanceResponse
            {
                LeaveTypeId = assignment.LeaveTypeId,
                LeaveTypeName = assignment.LeaveType.Name,
                IsPaid = assignment.LeaveType.IsPaid,
                AnnualQuotaDays = annualQuota,
                UsedDays = used,
                RemainingDays = remaining,
                Year = targetYear,
            });
        }

        return balances;
    }

    public async Task<IEnumerable<PublicHolidayResponse>> GetPublicHolidaysAsync(DateOnly? fromDate = null, DateOnly? toDate = null, bool includeInactive = false)
    {
        var query = _context.PublicHolidays.AsNoTracking().AsQueryable();

        if (!includeInactive)
            query = query.Where(h => h.IsActive);

        if (fromDate.HasValue)
            query = query.Where(h => h.Date >= fromDate.Value);

        if (toDate.HasValue)
            query = query.Where(h => h.Date <= toDate.Value);

        var holidays = await query
            .OrderBy(h => h.Date)
            .ToListAsync();

        return holidays.Select(MapToPublicHolidayResponse);
    }

    public async Task<PublicHolidayResponse> CreatePublicHolidayAsync(Guid actorId, CreatePublicHolidayRequest request)
    {
        await EnsureCanManageLeaveMasterDataAsync(actorId);

        var existsOnDate = await _context.PublicHolidays
            .AnyAsync(h => h.Date == request.Date);

        if (existsOnDate)
            throw new InvalidOperationException("A holiday already exists on this date.");

        var now = DateTime.Now;
        var holiday = new PublicHoliday
        {
            Id = Guid.NewGuid(),
            Date = request.Date,
            Name = request.Name.Trim(),
            Description = request.Description?.Trim(),
            IsActive = request.IsActive,
            CreatedAt = now,
            UpdatedAt = now,
        };

        _context.PublicHolidays.Add(holiday);
        await _context.SaveChangesAsync();

        return MapToPublicHolidayResponse(holiday);
    }

    public async Task<PublicHolidayResponse> UpdatePublicHolidayAsync(Guid actorId, Guid holidayId, UpdatePublicHolidayRequest request)
    {
        await EnsureCanManageLeaveMasterDataAsync(actorId);

        var holiday = await _context.PublicHolidays
            .FirstOrDefaultAsync(h => h.Id == holidayId)
            ?? throw new InvalidOperationException("Holiday not found.");

        var conflict = await _context.PublicHolidays
            .AnyAsync(h => h.Id != holidayId && h.Date == request.Date);

        if (conflict)
            throw new InvalidOperationException("A holiday already exists on this date.");

        holiday.Date = request.Date;
        holiday.Name = request.Name.Trim();
        holiday.Description = request.Description?.Trim();
        holiday.IsActive = request.IsActive;
        holiday.UpdatedAt = DateTime.Now;

        await _context.SaveChangesAsync();

        return MapToPublicHolidayResponse(holiday);
    }

    public async Task DeletePublicHolidayAsync(Guid actorId, Guid holidayId)
    {
        await EnsureCanManageLeaveMasterDataAsync(actorId);

        var holiday = await _context.PublicHolidays
            .FirstOrDefaultAsync(h => h.Id == holidayId)
            ?? throw new InvalidOperationException("Holiday not found.");

        _context.PublicHolidays.Remove(holiday);
        await _context.SaveChangesAsync();
    }

    private async Task<User> EnsureCanManageLeaveMasterDataAsync(Guid actorId)
    {
        var actor = await _userRepository.GetByIdAsync(actorId)
            ?? throw new InvalidOperationException("Actor not found.");

        var effectiveRole = EffectiveRoleResolver.GetEffectiveRole(actor);
        if (effectiveRole == UserRole.SuperAdmin)
            return actor;

        if (effectiveRole == UserRole.Admin && await _userService.HasPermissionAsync(actorId, PermissionSlugs.LeaveManagement))
            return actor;

        throw new InvalidOperationException("Only HR-Admin (leave_management permission) or Super Admin can manage leave masters.");
    }

    private async Task ValidateQuotaAsync(
        Guid userId,
        LeaveType leaveType,
        LeaveTypeAssignment assignment,
        int leaveYear,
        int deductedDays,
        bool includePending,
        Guid? excludeLeaveRequestId = null)
    {
        var annualQuota = assignment.AnnualQuotaDaysOverride ?? leaveType.AnnualQuotaDays;
        if (!annualQuota.HasValue)
            return;

        var usedDays = await GetUsedLeaveDaysAsync(
            userId,
            leaveType.Id,
            leaveType.Name,
            leaveYear,
            includePending,
            excludeLeaveRequestId);

        if (usedDays + deductedDays > annualQuota.Value)
        {
            var remaining = Math.Max(0, annualQuota.Value - usedDays);
            throw new InvalidOperationException($"Insufficient leave balance. Available: {remaining} day(s), requested: {deductedDays} day(s).");
        }
    }

    private async Task<int> GetUsedLeaveDaysAsync(
        Guid userId,
        Guid leaveTypeId,
        string? leaveTypeName,
        int leaveYear,
        bool includePending,
        Guid? excludeLeaveRequestId = null)
    {
        var normalizedTypeName = leaveTypeName?.Trim().ToLower();

        var query = _context.LeaveRequests
            .AsNoTracking()
            .Where(l =>
                l.UserId == userId
                && l.LeaveYear == leaveYear
                && (
                    l.LeaveTypeId == leaveTypeId
                    || (
                        l.LeaveTypeId == null
                        && normalizedTypeName != null
                        && l.LeaveTypeName != null
                        && l.LeaveTypeName.ToLower() == normalizedTypeName
                    )
                ));

        if (excludeLeaveRequestId.HasValue)
            query = query.Where(l => l.Id != excludeLeaveRequestId.Value);

        if (includePending)
        {
            query = query.Where(l => !NonActiveStatuses.Contains(l.Status));
        }
        else
        {
            query = query.Where(l => l.Status == LeaveStatus.Approved);
        }

        return await query.SumAsync(l => l.DeductedDays);
    }

    private async Task EnsureAssignmentsForLeaveTypeAsync(Guid leaveTypeId, DateTime? nowOverride = null)
    {
        var now = nowOverride ?? DateTime.Now;

        var eligibleUserIds = await _context.Users
            .AsNoTracking()
            .Where(u => u.Role != UserRole.SuperAdmin)
            .Select(u => u.Id)
            .ToListAsync();

        var assignedUserIds = await _context.LeaveTypeAssignments
            .AsNoTracking()
            .Where(a => a.LeaveTypeId == leaveTypeId)
            .Select(a => a.UserId)
            .ToListAsync();

        var assignedSet = assignedUserIds.ToHashSet();
        var missingUserIds = eligibleUserIds.Where(userId => !assignedSet.Contains(userId)).ToList();

        if (missingUserIds.Count == 0)
            return;

        var rows = missingUserIds.Select(userId => new LeaveTypeAssignment
        {
            Id = Guid.NewGuid(),
            LeaveTypeId = leaveTypeId,
            UserId = userId,
            AnnualQuotaDaysOverride = 0,
            CreatedAt = now,
            UpdatedAt = now,
        });

        _context.LeaveTypeAssignments.AddRange(rows);
        await _context.SaveChangesAsync();
    }

    private async Task EnsureAssignmentsForUserAsync(Guid userId)
    {
        var userExists = await _context.Users
            .AsNoTracking()
            .AnyAsync(u => u.Id == userId && u.Role != UserRole.SuperAdmin);

        if (!userExists)
            return;

        var activeLeaveTypeIds = await _context.LeaveTypes
            .AsNoTracking()
            .Where(t => t.IsActive)
            .Select(t => t.Id)
            .ToListAsync();

        if (activeLeaveTypeIds.Count == 0)
            return;

        var assignedTypeIds = await _context.LeaveTypeAssignments
            .AsNoTracking()
            .Where(a => a.UserId == userId)
            .Select(a => a.LeaveTypeId)
            .ToListAsync();

        var assignedSet = assignedTypeIds.ToHashSet();
        var missingTypeIds = activeLeaveTypeIds.Where(typeId => !assignedSet.Contains(typeId)).ToList();

        if (missingTypeIds.Count == 0)
            return;

        var now = DateTime.Now;
        var rows = missingTypeIds.Select(typeId => new LeaveTypeAssignment
        {
            Id = Guid.NewGuid(),
            LeaveTypeId = typeId,
            UserId = userId,
            AnnualQuotaDaysOverride = 0,
            CreatedAt = now,
            UpdatedAt = now,
        });

        _context.LeaveTypeAssignments.AddRange(rows);
        await _context.SaveChangesAsync();
    }

    private async Task<HashSet<DateOnly>> GetHolidayDatesAsync(DateOnly startDate, DateOnly endDate)
    {
        var holidayDates = await _context.PublicHolidays
            .AsNoTracking()
            .Where(h => h.IsActive && h.Date >= startDate && h.Date <= endDate)
            .Select(h => h.Date)
            .Distinct()
            .ToListAsync();

        return holidayDates.ToHashSet();
    }

    private static int CountSundaysInRange(DateOnly startDate, DateOnly endDate)
    {
        var sundayCount = 0;

        for (var date = startDate; date <= endDate; date = date.AddDays(1))
        {
            if (date.DayOfWeek == DayOfWeek.Sunday)
            {
                sundayCount++;
            }
        }

        return sundayCount;
    }

    private static int CountHolidayOverlapExcludingSundays(HashSet<DateOnly> holidayDates)
    {
        return holidayDates.Count(date => date.DayOfWeek != DayOfWeek.Sunday);
    }

    private static int CalculateInclusiveDays(DateOnly startDate, DateOnly endDate)
        => endDate.DayNumber - startDate.DayNumber + 1;

    private async Task<LeaveResponse> MapToResponseWithRecalculatedMetricsAsync(LeaveRequest leave)
    {
        var response = MapToResponse(leave);
        var (requestedDays, holidayOverlapDays, deductedDays) = await CalculateLeaveDayMetricsAsync(leave.StartDate, leave.EndDate);
        response.RequestedDays = requestedDays;
        response.HolidayOverlapDays = holidayOverlapDays;
        response.DeductedDays = deductedDays;
        return response;
    }

    private static LeaveResponse MapToResponse(LeaveRequest leave)
    {
        return new LeaveResponse
        {
            Id = leave.Id,
            UserId = leave.UserId,
            User = leave.User != null ? new UserResponse
            {
                Id = leave.User.Id,
                Username = leave.User.Username,
                Role = EffectiveRoleResolver.GetEffectiveRole(leave.User),
                FirstName = leave.User.FirstName,
                LastName = leave.User.LastName,
                ProfilePictureUrl = leave.User.ProfilePictureUrl,
            } : null!,
            StartDate = leave.StartDate,
            EndDate = leave.EndDate,
            LeaveTypeId = leave.LeaveTypeId,
            LeaveTypeName = leave.LeaveType?.Name ?? leave.LeaveTypeName,
            RequestedDays = leave.RequestedDays,
            HolidayOverlapDays = leave.HolidayOverlapDays,
            DeductedDays = leave.DeductedDays,
            LeaveYear = leave.LeaveYear,
            Reason = leave.Reason,
            Status = leave.Status,
            CurrentApprovalLevel = leave.CurrentApprovalLevel,
            CreatedAt = leave.CreatedAt,
            RejectionReason = leave.RejectionReason,
            TargetPillerId = leave.TargetPillerId,
            TargetPillerName = leave.TargetPiller != null ? $"{leave.TargetPiller.FirstName} {leave.TargetPiller.LastName}".Trim() : null,
            OwningSuperAdminId = leave.OwningSuperAdminId,
            OwningSuperAdminName = leave.OwningSuperAdmin != null ? $"{leave.OwningSuperAdmin.FirstName} {leave.OwningSuperAdmin.LastName}".Trim() : null,
            PillerApprovedById = leave.PillerApprovedById,
            PillerApprovedByName = leave.PillerApprovedBy != null ? $"{leave.PillerApprovedBy.FirstName} {leave.PillerApprovedBy.LastName}".Trim() : null,
            AdminApprovedById = leave.AdminApprovedById,
            AdminApprovedByName = leave.AdminApprovedBy != null ? $"{leave.AdminApprovedBy.FirstName} {leave.AdminApprovedBy.LastName}".Trim() : null,
            SuperAdminApprovedById = leave.SuperAdminApprovedById,
            SuperAdminApprovedByName = leave.SuperAdminApprovedBy != null ? $"{leave.SuperAdminApprovedBy.FirstName} {leave.SuperAdminApprovedBy.LastName}".Trim() : null,
            RejectedById = leave.RejectedById,
            RejectedByName = leave.RejectedBy != null ? $"{leave.RejectedBy.FirstName} {leave.RejectedBy.LastName}".Trim() : null,
        };
    }

    private static LeaveTypeResponse MapToLeaveTypeResponse(LeaveType leaveType, int assignedEmployeesCount)
    {
        return new LeaveTypeResponse
        {
            Id = leaveType.Id,
            Name = leaveType.Name,
            Description = leaveType.Description,
            IsPaid = leaveType.IsPaid,
            IsActive = leaveType.IsActive,
            AnnualQuotaDays = leaveType.AnnualQuotaDays,
            CreatedAt = leaveType.CreatedAt,
            UpdatedAt = leaveType.UpdatedAt,
            AssignedEmployeesCount = assignedEmployeesCount,
        };
    }

    private async Task<LeaveTypeAssignmentResponse> MapToLeaveTypeAssignmentResponseAsync(LeaveTypeAssignment assignment, int? defaultQuota)
    {
        var year = DateTime.Now.Year;
        var effectiveQuota = assignment.AnnualQuotaDaysOverride ?? defaultQuota ?? 0;
        var usedDays = await GetUsedLeaveDaysAsync(
            assignment.UserId,
            assignment.LeaveTypeId,
            assignment.LeaveType.Name,
            year,
            includePending: false);
        var remainingDays = Math.Max(0, effectiveQuota - usedDays);

        return new LeaveTypeAssignmentResponse
        {
            Id = assignment.Id,
            LeaveTypeId = assignment.LeaveTypeId,
            LeaveTypeName = assignment.LeaveType.Name,
            UserId = assignment.UserId,
            User = new UserResponse
            {
                Id = assignment.User.Id,
                Username = assignment.User.Username,
                Role = EffectiveRoleResolver.GetEffectiveRole(assignment.User),
                FirstName = assignment.User.FirstName,
                LastName = assignment.User.LastName,
                ProfilePictureUrl = assignment.User.ProfilePictureUrl,
            },
            AnnualQuotaDaysOverride = assignment.AnnualQuotaDaysOverride,
            EffectiveAnnualQuotaDays = effectiveQuota,
            UsedDays = usedDays,
            RemainingDays = remainingDays,
            Year = year,
            CreatedAt = assignment.CreatedAt,
            UpdatedAt = assignment.UpdatedAt,
        };
    }

    private static PublicHolidayResponse MapToPublicHolidayResponse(PublicHoliday holiday)
    {
        return new PublicHolidayResponse
        {
            Id = holiday.Id,
            Date = holiday.Date,
            Name = holiday.Name,
            Description = holiday.Description,
            IsActive = holiday.IsActive,
            CreatedAt = holiday.CreatedAt,
            UpdatedAt = holiday.UpdatedAt,
        };
    }
}
