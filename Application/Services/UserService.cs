using vision_backend.Application.DTOs.Users;
using vision_backend.Application.DTOs.Common;
using vision_backend.Application.Constants;
using vision_backend.Application.Interfaces;
using vision_backend.Domain.Entities;
using vision_backend.Domain.Enums;
using vision_backend.Infrastructure.Data;
using vision_backend.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace vision_backend.Application.Services;

public class UserService : IUserService
{
    private readonly IUserRepository _userRepository;
    private readonly IStorageService _storage;
    private readonly INotificationService _notificationService;
    private readonly ApplicationDbContext _context;
    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg", "image/png", "image/gif", "image/webp"
    };
    private const long MaxFileSize = 5 * 1024 * 1024; // 5 MB

    public UserService(IUserRepository userRepository, IStorageService storage, INotificationService notificationService, ApplicationDbContext context)
    {
        _userRepository = userRepository;
        _storage = storage;
        _notificationService = notificationService;
        _context = context;
    }

    // ... (Existing methods) ...

    public async Task DeleteUserAsync(Guid userId, Guid deleterId, UserRole deleterRole)
    {
        var executionStrategy = _context.Database.CreateExecutionStrategy();

        await executionStrategy.ExecuteAsync(async () =>
        {
            var user = await _userRepository.GetByIdAsync(userId);
            if (user == null)
            {
                throw new InvalidOperationException("User not found.");
            }

            if (userId == deleterId)
                throw new InvalidOperationException("You cannot delete your own account.");

            if (!CanDeleteRole(deleterRole, user.Role))
            {
                throw new InvalidOperationException("Not authorized to delete this user.");
            }

            var deleterExists = await _context.Users.AnyAsync(u => u.Id == deleterId);
            if (!deleterExists)
                throw new InvalidOperationException("Deleting user context not found.");

            await using var transaction = await _context.Database.BeginTransactionAsync();

            var now = DateTime.Now;
            await _context.Vouchers
                .Where(v => v.CreatedById == userId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(v => v.CreatedById, deleterId)
                    .SetProperty(v => v.UpdatedAt, now));

            await _context.LeaveRequests
                .Where(l => l.UserId == userId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(l => l.UserId, deleterId)
                    .SetProperty(l => l.UpdatedAt, now));

            await _userRepository.DeleteAsync(userId);

            await transaction.CommitAsync();
        });
    }

    public async Task<PagedResult<UserResponse>> GetUsersPagedAsync(UserSearchRequest request)
    {
        var (users, totalCount) = await _userRepository.GetPagedAsync(
            request.Page, 
            request.PageSize, 
            request.SearchTerm, 
            request.SortColumn, 
            request.SortOrder,
            request.RoleFilter,
            request.ExcludeUserId);

        var mapped = new List<UserResponse>(users.Count);
        foreach (var user in users)
        {
            mapped.Add(await MapUserAsync(user));
        }

        return new PagedResult<UserResponse>
        {
            Items = mapped.ToList(),
            TotalCount = totalCount,
            Page = request.Page,
            PageSize = request.PageSize
        };
    }

    public async Task<UserResponse> CreateUserAsync(Guid creatorId, UserRole creatorRole, CreateUserRequest request)
    {
        if (!CanCreateRole(creatorRole, request.Role))
        {
            throw new InvalidOperationException("Not authorized to create this role.");
        }

        if (await _userRepository.ExistsAsync(request.Username))
        {
            throw new InvalidOperationException("Username already exists.");
        }

        var creator = await _userRepository.GetByIdAsync(creatorId)
            ?? throw new InvalidOperationException("Creator not found.");

        if (request.Role == UserRole.Admin && !request.AdminRoleId.HasValue)
            throw new InvalidOperationException("Admin role is required when creating an Admin user.");

        Guid? assignedSuperAdminId = request.SuperAdminId;
        if (request.Role != UserRole.SuperAdmin)
        {
            assignedSuperAdminId = creator.Role == UserRole.SuperAdmin
                ? creator.Id
                : creator.SuperAdminId;
        }

        var user = new User
        {
            Id = Guid.NewGuid(),
            Username = request.Username,
            Role = request.Role,
            Balance = request.InitialBalance ?? 0m,
            FirstName = request.FirstName,
            LastName = request.LastName,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword($"{request.Username}@123"),
            DateOfBirth = request.DateOfBirth,
            MobileNumber = request.MobileNumber,
            IsFirstLogin = true,
            Address = request.Address,
            EmergencyContactNo = request.EmergencyContactNo,
            AadharCardNo = request.AadharCardNo,
            PanCardNo = request.PanCardNo,
            BloodGroup = request.BloodGroup,
            DesignationId = request.DesignationId,
            JoiningDate = request.JoiningDate,
            PfNo = request.PfNo,
            EsicNo = request.EsicNo,
            BankName = request.BankName,
            IfscCode = request.IfscCode,
            AccountNumber = request.AccountNumber,
            BankBranch = request.BankBranch,
            AdminRoleId = request.AdminRoleId,
            SuperAdminId = assignedSuperAdminId,
        };

        await _userRepository.CreateAsync(user);

        if (user.Role != UserRole.SuperAdmin)
        {
            var activeLeaveTypeIds = await _context.LeaveTypes
                .AsNoTracking()
                .Where(t => t.IsActive)
                .Select(t => t.Id)
                .ToListAsync();

            if (activeLeaveTypeIds.Count > 0)
            {
                var now = DateTime.Now;
                var leaveAssignments = activeLeaveTypeIds.Select(leaveTypeId => new LeaveTypeAssignment
                {
                    Id = Guid.NewGuid(),
                    LeaveTypeId = leaveTypeId,
                    UserId = user.Id,
                    AnnualQuotaDaysOverride = 0,
                    CreatedAt = now,
                    UpdatedAt = now,
                });

                _context.LeaveTypeAssignments.AddRange(leaveAssignments);
                await _context.SaveChangesAsync();
            }
        }

        if (user.Balance > 0)
        {
            await RecordBalanceTransactionAsync(
                userId: user.Id,
                type: BalanceTransactionType.Credit,
                amount: user.Balance,
                balanceAfter: user.Balance,
                entryDate: DateTime.Now,
                description: "Initial balance",
                referenceType: "UserCreate",
                referenceId: null,
                createdById: creatorId);
        }

        return await MapUserAsync(user);
    }

    public async Task<UserResponse> UpdateProfileAsync(Guid userId, UpdateProfileRequest request)
    {
        var user = await _userRepository.GetByIdAsync(userId);
        if (user == null)
        {
            throw new InvalidOperationException("User not found.");
        }

        user.FirstName = request.FirstName;
        user.LastName = request.LastName;
        user.DateOfBirth = request.DateOfBirth;
        user.MobileNumber = request.MobileNumber;
        user.Address = request.Address;
        user.EmergencyContactNo = request.EmergencyContactNo;
        user.AadharCardNo = request.AadharCardNo;
        user.PanCardNo = request.PanCardNo;
        user.BloodGroup = request.BloodGroup;
        user.DesignationId = request.DesignationId;
        user.JoiningDate = request.JoiningDate;
        user.PfNo = request.PfNo;
        user.EsicNo = request.EsicNo;
        user.BankName = request.BankName;
        user.IfscCode = request.IfscCode;
        user.AccountNumber = request.AccountNumber;
        user.BankBranch = request.BankBranch;

        await _userRepository.UpdateAsync(user);

        return await MapUserAsync(user);
    }

    public async Task<UserResponse> UpdateBalanceAsync(Guid userId, decimal amount, string mode, UserRole updaterRole, Guid updaterId)
    {
        if (updaterRole != UserRole.SuperAdmin && updaterRole != UserRole.Admin)
        {
            throw new InvalidOperationException("Not authorized to update balance.");
        }

        var user = await _userRepository.GetByIdAsync(userId);
        if (user == null)
        {
            throw new InvalidOperationException("User not found.");
        }

        var normalizedMode = string.IsNullOrWhiteSpace(mode) ? "set" : mode.Trim().ToLowerInvariant();
        var oldBalance = user.Balance;

        if (normalizedMode == "add")
        {
            user.Balance += amount;
        }
        else
        {
            user.Balance = amount;
        }

        await _userRepository.UpdateAsync(user);

        var delta = user.Balance - oldBalance;
        if (delta != 0)
        {
            var type = delta > 0 ? BalanceTransactionType.Credit : BalanceTransactionType.Debit;
            var absAmount = Math.Abs(delta);
            var referenceType = normalizedMode == "add" ? "BalanceAdd" : "BalanceSet";
            var description = normalizedMode == "add"
                ? "Balance added"
                : delta > 0
                    ? "Balance increased"
                    : "Balance reduced";

            await RecordBalanceTransactionAsync(
                userId: user.Id,
                type: type,
                amount: absAmount,
                balanceAfter: user.Balance,
                entryDate: DateTime.Now,
                description: description,
                referenceType: referenceType,
                referenceId: null,
                createdById: updaterId);

            var title = delta > 0 ? "Balance credited" : "Balance debited";
            var direction = delta > 0 ? "credited" : "debited";
            await _notificationService.CreateAsync(
                user.Id,
                "balance_update",
                title,
                $"Your account was {direction} by {absAmount:N2}. Current balance: {user.Balance:N2}.",
                "balance",
                null);
        }

        return await MapUserAsync(user);
    }

    private async Task RecordBalanceTransactionAsync(
        Guid userId,
        BalanceTransactionType type,
        decimal amount,
        decimal balanceAfter,
        DateTime entryDate,
        string description,
        string? referenceType,
        Guid? referenceId,
        Guid? createdById)
    {
        _context.BalanceTransactions.Add(new BalanceTransaction
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Type = type,
            Amount = amount,
            BalanceAfter = balanceAfter,
            EntryDate = entryDate,
            Description = description,
            ReferenceType = referenceType,
            ReferenceId = referenceId,
            CreatedById = createdById,
        });

        await _context.SaveChangesAsync();
    }

    public async Task<UserResponse> GetUserAsync(Guid userId)
    {
        var user = await _userRepository.GetByIdAsync(userId);
        if (user == null)
        {
            throw new InvalidOperationException("User not found.");
        }

        return await MapUserAsync(user);
    }

    public async Task<List<UserResponse>> GetAllUsersAsync()
    {
        var users = await _userRepository.GetAllAsync();
        var mapped = new List<UserResponse>(users.Count);
        foreach (var user in users)
        {
            mapped.Add(await MapUserAsync(user));
        }

        return mapped;
    }
    
    // ... Helpers ...

    private static bool CanDeleteRole(UserRole deleterRole, UserRole targetRole)
    {
        if (deleterRole == UserRole.SuperAdmin) return true; // SuperAdmin can delete anyone
        if (deleterRole == UserRole.Admin) return targetRole == UserRole.Piller || targetRole == UserRole.GeneralUser || targetRole == UserRole.Admin;
        return false;
    }

    private static bool CanCreateRole(UserRole creatorRole, UserRole targetRole)
    {
        return creatorRole switch
        {
            UserRole.SuperAdmin => targetRole == UserRole.SuperAdmin || targetRole == UserRole.Admin || targetRole == UserRole.Piller || targetRole == UserRole.GeneralUser,
            UserRole.Admin => targetRole == UserRole.Piller || targetRole == UserRole.GeneralUser || targetRole == UserRole.Admin,
            UserRole.Piller => targetRole == UserRole.GeneralUser,
            _ => false
        };
    }

    public async Task<UserResponse> UploadProfilePictureAsync(Guid userId, Stream fileStream, string fileName, string contentType)
    {
        if (!AllowedContentTypes.Contains(contentType))
            throw new InvalidOperationException("Invalid file type. Only JPEG, PNG, GIF, and WebP are allowed.");

        if (fileStream.Length > MaxFileSize)
            throw new InvalidOperationException("File size exceeds the maximum limit of 5 MB.");

        var user = await _userRepository.GetByIdAsync(userId)
            ?? throw new InvalidOperationException("User not found.");

        // Delete old S3 object if a previous picture exists
        if (!string.IsNullOrEmpty(user.ProfilePictureUrl))
        {
            await _storage.DeleteAsync(user.ProfilePictureUrl);
        }

        // Upload to S3 — key pattern: profiles/{userId}_{guid}{ext}
        var extension = Path.GetExtension(fileName);
        var s3Key = $"profiles/{userId}_{Guid.NewGuid():N}{extension}";
        var storedKey = await _storage.UploadAsync(fileStream, s3Key, contentType);

        // Persist the S3 object key (not a URL) in the database
        user.ProfilePictureUrl = storedKey;
        await _userRepository.UpdateAsync(user);

        return await MapUserAsync(user);
    }

    public async Task<UserResponse> DeleteProfilePictureAsync(Guid userId)
    {
        var user = await _userRepository.GetByIdAsync(userId)
            ?? throw new InvalidOperationException("User not found.");

        if (!string.IsNullOrEmpty(user.ProfilePictureUrl))
        {
            await _storage.DeleteAsync(user.ProfilePictureUrl);
            user.ProfilePictureUrl = null;
            await _userRepository.UpdateAsync(user);
        }

        return await MapUserAsync(user);
    }

    public async Task<UserResponse> UpdateUserRoleAsync(Guid actorUserId, Guid userId, UserRole role, Guid? adminRoleId)
    {
        var actor = await _userRepository.GetByIdAsync(actorUserId)
            ?? throw new InvalidOperationException("Actor not found.");

        if (!CanCreateRole(actor.Role, role))
            throw new InvalidOperationException("Not authorized to assign this role.");

        var user = await _userRepository.GetByIdAsync(userId)
            ?? throw new InvalidOperationException("User not found.");

        if (role == UserRole.Admin)
        {
            if (!adminRoleId.HasValue)
                throw new InvalidOperationException("Admin role is required for Admin users.");

            var roleExists = await _context.AdminRoles.AnyAsync(r => r.Id == adminRoleId.Value);
            if (!roleExists)
                throw new InvalidOperationException("Selected admin role not found.");

            user.AdminRoleId = adminRoleId;
        }
        else
        {
            user.AdminRoleId = null;
        }

        user.Role = role;

        if (role == UserRole.SuperAdmin)
        {
            user.SuperAdminId = null;
        }
        else
        {
            user.SuperAdminId = actor.Role == UserRole.SuperAdmin
                ? actor.Id
                : actor.SuperAdminId;
        }

        await _userRepository.UpdateAsync(user);
        return await MapUserAsync(user);
    }

    public async Task AdminResetPasswordAsync(Guid userId, string newPassword)
    {
        var user = await _userRepository.GetByIdAsync(userId)
            ?? throw new InvalidOperationException("User not found.");

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
        user.IsFirstLogin = true;

        await _userRepository.UpdateAsync(user);
    }

    /// <summary>
    /// Maps a User entity to a UserResponse DTO.
    /// If <c>ProfilePictureUrl</c> is an S3 object key (does not start with "http"),
    /// it is converted to a 1-hour pre-signed HTTPS URL on-the-fly.
    /// </summary>
    private async Task<UserResponse> MapUserAsync(User user)
    {
        var dbUser = await _userRepository.GetByIdAsync(user.Id) ?? user;
        var modules = await GetEffectivePermissionsAsync(dbUser.Id);

        return new UserResponse
        {
            Id = dbUser.Id,
            Username = dbUser.Username,
            Role = EffectiveRoleResolver.GetEffectiveRole(dbUser),
            Balance = dbUser.Balance,
            FirstName = dbUser.FirstName,
            LastName = dbUser.LastName,
            DateOfBirth = dbUser.DateOfBirth,
            MobileNumber = dbUser.MobileNumber,
            IsFirstLogin = dbUser.IsFirstLogin,
            ProfilePictureUrl = ResolveUrl(dbUser.ProfilePictureUrl),
            Address = dbUser.Address,
            EmergencyContactNo = dbUser.EmergencyContactNo,
            AadharCardNo = dbUser.AadharCardNo,
            PanCardNo = dbUser.PanCardNo,
            BloodGroup = dbUser.BloodGroup,
            DesignationId = dbUser.DesignationId,
            DesignationName = dbUser.Designation?.Name,
            JoiningDate = dbUser.JoiningDate,
            PfNo = dbUser.PfNo,
            EsicNo = dbUser.EsicNo,
            BankName = dbUser.BankName,
            IfscCode = dbUser.IfscCode,
            AccountNumber = dbUser.AccountNumber,
            BankBranch = dbUser.BankBranch,
            AdminRoleId = dbUser.AdminRoleId,
            AdminRoleName = dbUser.AdminRole?.Name,
            SuperAdminId = dbUser.SuperAdminId,
            ModuleAccess = modules,
        };
    }

    /// <summary>
    /// Returns a pre-signed URL when the stored value is an S3 key,
    /// or null when no picture has been set.
    /// </summary>
    private string? ResolveUrl(string? key)
        => string.IsNullOrEmpty(key) ? null : _storage.GetPresignedUrl(key);

    public async Task<List<string>> GetEffectivePermissionsAsync(Guid userId)
    {
        var user = await _userRepository.GetByIdAsync(userId)
            ?? throw new InvalidOperationException("User not found.");

        var effectiveRole = EffectiveRoleResolver.GetEffectiveRole(user);

        if (effectiveRole == UserRole.SuperAdmin)
        {
            var all = await _context.Permissions.AsNoTracking().Select(p => p.Slug).ToListAsync();
            return all.Count > 0 ? all.Distinct(StringComparer.Ordinal).ToList() : PermissionSlugs.All.ToList();
        }

        if (effectiveRole == UserRole.Admin)
        {
            if (!user.AdminRoleId.HasValue)
                return PermissionSlugs.BaselineForAllEmployees.ToList();

            var adminManaged = await _context.RolePermissions
                .AsNoTracking()
                .Where(rp => rp.RoleId == user.AdminRoleId.Value)
                .Select(rp => rp.Permission.Slug)
                .Distinct()
                .ToListAsync();

            return PermissionSlugs.BaselineForAllEmployees
                .Concat(adminManaged)
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }

        if (effectiveRole == UserRole.Piller)
        {
            Guid? roleIdForPermissions = user.AdminRoleId;

            if (!roleIdForPermissions.HasValue)
            {
                var normalizedPiller = EffectiveRoleResolver.PillerRoleName;

                roleIdForPermissions = await _context.AdminRoles
                    .AsNoTracking()
                    .Where(r => r.Name.ToLower() == normalizedPiller)
                    .OrderBy(r => r.CreatedBy == user.SuperAdminId ? 0 : 1)
                    .ThenBy(r => r.Name)
                    .Select(r => (Guid?)r.Id)
                    .FirstOrDefaultAsync();
            }

            if (!roleIdForPermissions.HasValue)
                return PermissionSlugs.BaselineForAllEmployees.ToList();

            var pillerManaged = await _context.RolePermissions
                .AsNoTracking()
                .Where(rp => rp.RoleId == roleIdForPermissions.Value)
                .Select(rp => rp.Permission.Slug)
                .Distinct()
                .ToListAsync();

            return PermissionSlugs.BaselineForAllEmployees
                .Concat(pillerManaged)
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }

        return PermissionSlugs.BaselineForAllEmployees.ToList();
    }

    public async Task<bool> HasPermissionAsync(Guid userId, string permissionSlug)
    {
        if (!PermissionSlugs.All.Contains(permissionSlug))
            return false;

        var effective = await GetEffectivePermissionsAsync(userId);
        return effective.Contains(permissionSlug, StringComparer.Ordinal);
    }

    public async Task<List<AdminRoleResponse>> GetAdminRolesAsync(Guid superAdminId)
    {
        var caller = await _userRepository.GetByIdAsync(superAdminId)
            ?? throw new InvalidOperationException("Caller not found.");

        var hasRoleManagement = caller.Role == UserRole.SuperAdmin
            || await HasPermissionAsync(superAdminId, PermissionSlugs.RoleManagement);
        var hasUserManagement = caller.Role == UserRole.SuperAdmin
            || await HasPermissionAsync(superAdminId, PermissionSlugs.UserManagement);

        if (!hasRoleManagement && !hasUserManagement)
            throw new InvalidOperationException("Not authorized to view roles.");

        await EnsureCorePermissionCatalogAsync();

        var roles = await _context.AdminRoles
            .AsNoTracking()
            .Include(r => r.RolePermissions)
                .ThenInclude(rp => rp.Permission)
            .OrderBy(r => r.Name)
            .ToListAsync();

        return roles.Select(MapRole).ToList();
    }

    public async Task<AdminRoleResponse> CreateAdminRoleAsync(Guid superAdminId, CreateAdminRoleRequest request)
    {
        var caller = await _userRepository.GetByIdAsync(superAdminId)
            ?? throw new InvalidOperationException("Caller not found.");

        var hasRoleManagement = caller.Role == UserRole.SuperAdmin
            || await HasPermissionAsync(superAdminId, PermissionSlugs.RoleManagement);

        if (!hasRoleManagement)
            throw new InvalidOperationException("Not authorized to create roles.");

        var roleName = request.Name.Trim();
        if (string.IsNullOrWhiteSpace(roleName))
            throw new InvalidOperationException("Role name is required.");

        if (IsProtectedRoleName(roleName))
            throw new InvalidOperationException("Superadmin role is system-protected and cannot be created manually.");

        var exists = await _context.AdminRoles.AnyAsync(r => r.Name.ToLower() == roleName.ToLower());
        if (exists)
            throw new InvalidOperationException("Role with this name already exists.");

        var role = new AdminRole
        {
            Id = Guid.NewGuid(),
            Name = roleName,
            CreatedBy = superAdminId,
            CreatedAt = DateTime.Now,
        };

        _context.AdminRoles.Add(role);
        await _context.SaveChangesAsync();

        await UpdateAdminRolePermissionsAsync(superAdminId, role.Id, request.PermissionSlugs ?? new List<string>());

        var created = await _context.AdminRoles
            .AsNoTracking()
            .Include(r => r.RolePermissions)
                .ThenInclude(rp => rp.Permission)
            .FirstAsync(r => r.Id == role.Id);

        return MapRole(created);
    }

    public async Task<AdminRoleResponse> UpdateAdminRoleAsync(Guid superAdminId, Guid roleId, UpdateAdminRoleRequest request)
    {
        var caller = await _userRepository.GetByIdAsync(superAdminId)
            ?? throw new InvalidOperationException("Caller not found.");

        var hasRoleManagement = caller.Role == UserRole.SuperAdmin
            || await HasPermissionAsync(superAdminId, PermissionSlugs.RoleManagement);

        if (!hasRoleManagement)
            throw new InvalidOperationException("Not authorized to update roles.");

        var role = await _context.AdminRoles.FirstOrDefaultAsync(r => r.Id == roleId)
            ?? throw new InvalidOperationException("Role not found.");

        if (IsProtectedRoleName(role.Name))
            throw new InvalidOperationException("Superadmin role is system-protected and cannot be edited.");

        var name = request.Name.Trim();
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("Role name is required.");

        var duplicate = await _context.AdminRoles.AnyAsync(r => r.Id != roleId && r.Name.ToLower() == name.ToLower());
        if (duplicate)
            throw new InvalidOperationException("Role with this name already exists.");

        role.Name = name;
        await _context.SaveChangesAsync();

        var updated = await _context.AdminRoles
            .AsNoTracking()
            .Include(r => r.RolePermissions)
                .ThenInclude(rp => rp.Permission)
            .FirstAsync(r => r.Id == roleId);

        return MapRole(updated);
    }

    public async Task<AdminRoleResponse> UpdateAdminRolePermissionsAsync(Guid superAdminId, Guid roleId, List<string> permissionSlugs)
    {
        var caller = await _userRepository.GetByIdAsync(superAdminId)
            ?? throw new InvalidOperationException("Caller not found.");

        var hasRoleManagement = caller.Role == UserRole.SuperAdmin
            || await HasPermissionAsync(superAdminId, PermissionSlugs.RoleManagement);

        if (!hasRoleManagement)
            throw new InvalidOperationException("Not authorized to configure role permissions.");

        var role = await _context.AdminRoles.FirstOrDefaultAsync(r => r.Id == roleId)
            ?? throw new InvalidOperationException("Role not found.");

        if (IsProtectedRoleName(role.Name))
            throw new InvalidOperationException("Superadmin role is system-protected and cannot be edited.");

        await EnsureCorePermissionCatalogAsync();

        var normalizedSlugs = permissionSlugs
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var invalid = normalizedSlugs.Where(s => !PermissionSlugs.ManageableInRoleManagement.Contains(s)).ToList();
        if (invalid.Count > 0)
            throw new InvalidOperationException($"Invalid permission slugs: {string.Join(", ", invalid)}");

        var permissions = await _context.Permissions
            .Where(p => normalizedSlugs.Contains(p.Slug))
            .ToListAsync();

        var existing = await _context.RolePermissions.Where(rp => rp.RoleId == roleId).ToListAsync();
        _context.RolePermissions.RemoveRange(existing);

        var newLinks = permissions.Select(p => new RolePermission
        {
            Id = Guid.NewGuid(),
            RoleId = roleId,
            PermissionId = p.Id,
        });

        await _context.RolePermissions.AddRangeAsync(newLinks);
        await _context.SaveChangesAsync();

        var updated = await _context.AdminRoles
            .AsNoTracking()
            .Include(r => r.RolePermissions)
                .ThenInclude(rp => rp.Permission)
            .FirstAsync(r => r.Id == roleId);

        return MapRole(updated);
    }

    public async Task DeleteAdminRoleAsync(Guid actorUserId, Guid roleId)
    {
        var caller = await _userRepository.GetByIdAsync(actorUserId)
            ?? throw new InvalidOperationException("Caller not found.");

        var hasRoleManagement = caller.Role == UserRole.SuperAdmin
            || await HasPermissionAsync(actorUserId, PermissionSlugs.RoleManagement);

        if (!hasRoleManagement)
            throw new InvalidOperationException("Not authorized to delete roles.");

        var role = await _context.AdminRoles.FirstOrDefaultAsync(r => r.Id == roleId)
            ?? throw new InvalidOperationException("Role not found.");

        if (IsProtectedRoleName(role.Name))
            throw new InvalidOperationException("Superadmin role is system-protected and cannot be deleted.");

        var hasUsers = await _context.Users.AnyAsync(u => u.AdminRoleId == roleId);
        if (hasUsers)
            throw new InvalidOperationException("Cannot delete role because users are currently assigned to it.");

        _context.AdminRoles.Remove(role);
        await _context.SaveChangesAsync();
    }

    public async Task<UserResponse> AssignAdminRoleAsync(Guid adminUserId, Guid roleId)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == adminUserId)
            ?? throw new InvalidOperationException("User not found.");

        if (user.Role != UserRole.Admin)
            throw new InvalidOperationException("Role assignment is only valid for Admin users.");

        var role = await _context.AdminRoles.FirstOrDefaultAsync(r => r.Id == roleId)
            ?? throw new InvalidOperationException("Admin role not found.");

        user.AdminRoleId = role.Id;
        await _context.SaveChangesAsync();

        var updated = await _userRepository.GetByIdAsync(adminUserId)
            ?? throw new InvalidOperationException("User not found after update.");

        return await MapUserAsync(updated);
    }

    public async Task<UserResponse> AssignPillerToSuperAdminAsync(Guid pillerId, Guid? superAdminId)
    {
        var piller = await _context.Users.FirstOrDefaultAsync(u => u.Id == pillerId)
            ?? throw new InvalidOperationException("Piller user not found.");

        var pillerWithRole = await _userRepository.GetByIdAsync(pillerId) ?? piller;
        if (!EffectiveRoleResolver.IsEffectivePiller(pillerWithRole))
            throw new InvalidOperationException("Only piller users can be assigned to SuperAdmin.");

        if (superAdminId.HasValue)
        {
            var superAdmin = await _context.Users.FirstOrDefaultAsync(u => u.Id == superAdminId.Value)
                ?? throw new InvalidOperationException("SuperAdmin user not found.");

            if (superAdmin.Role != UserRole.SuperAdmin)
                throw new InvalidOperationException("Owner must be a SuperAdmin.");
        }

        piller.SuperAdminId = superAdminId;
        await _context.SaveChangesAsync();

        var updated = await _userRepository.GetByIdAsync(pillerId)
            ?? throw new InvalidOperationException("Piller not found after update.");

        return await MapUserAsync(updated);
    }

    private static AdminRoleResponse MapRole(AdminRole role)
    {
        return new AdminRoleResponse
        {
            Id = role.Id,
            Name = role.Name,
            CreatedBy = role.CreatedBy,
            PermissionSlugs = role.RolePermissions.Select(rp => rp.Permission.Slug).Distinct(StringComparer.Ordinal).ToList(),
        };
    }

    private static bool IsProtectedRoleName(string roleName)
    {
        var normalized = roleName.Trim().Replace(" ", string.Empty, StringComparison.Ordinal).ToLowerInvariant();
        return normalized == "superadmin";
    }

    private async Task EnsureCorePermissionCatalogAsync()
    {
        var requiredPermissions = new[]
        {
            new { Name = "General Dashboard", Slug = PermissionSlugs.DashboardGeneral },
            new { Name = "Analytical Dashboard", Slug = PermissionSlugs.DashboardAnalytical },
            new { Name = "Leave Approvals", Slug = PermissionSlugs.LeaveApprovals },
            new { Name = "Leave Management", Slug = PermissionSlugs.LeaveManagement },
            new { Name = "Upcoming Leaves", Slug = PermissionSlugs.UpcomingLeaves },
            new { Name = "Voucher Management", Slug = PermissionSlugs.VoucherManagement },
            new { Name = "Voucher Categories", Slug = PermissionSlugs.VoucherCategories },
            new { Name = "User Management", Slug = PermissionSlugs.UserManagement },
            new { Name = "Site Management", Slug = PermissionSlugs.SiteManagement },
            new { Name = "Designation Management", Slug = PermissionSlugs.DesignationManagement },
            new { Name = "Role Management", Slug = PermissionSlugs.RoleManagement },
        };

        var slugs = requiredPermissions.Select(p => p.Slug).ToList();
        var existingSlugs = await _context.Permissions
            .AsNoTracking()
            .Where(p => slugs.Contains(p.Slug))
            .Select(p => p.Slug)
            .ToListAsync();

        var missing = requiredPermissions
            .Where(p => !existingSlugs.Contains(p.Slug, StringComparer.Ordinal))
            .ToList();

        if (missing.Count == 0)
            return;

        foreach (var permission in missing)
        {
            _context.Permissions.Add(new Permission
            {
                Id = Guid.NewGuid(),
                Name = permission.Name,
                Slug = permission.Slug,
            });
        }

        await _context.SaveChangesAsync();
    }
}
