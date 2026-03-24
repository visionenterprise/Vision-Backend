using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using vision_backend.Application.Constants;
using vision_backend.Domain.Entities;
using vision_backend.Domain.Enums;
using vision_backend.Infrastructure.Data;

namespace vision_backend.Extensions;

public static class SeedDataExtensions
{
    public static async Task SeedInitialUsersAsync(this IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();

        if (context.Database.IsRelational())
        {
            await context.Database.MigrateAsync();

            var truncateOnStart = configuration.GetValue<bool>("Seeder:TruncateOnStart");
            if (truncateOnStart)
            {
                await context.Database.ExecuteSqlRawAsync(@"
                    TRUNCATE TABLE
                        ""BalanceTransactions"",
                        ""RolePermissions"",
                        ""AdminRoles"",
                        ""Permissions"",
                        ""LeaveRequests"",
                        ""LeaveTypeAssignments"",
                        ""PublicHolidays"",
                        ""LeaveTypes"",
                        ""Vouchers"",
                        ""RefreshTokens"",
                        ""Users"",
                        ""Sites"",
                        ""VoucherCategories"",
                        ""Designations""
                    RESTART IDENTITY CASCADE;
                ");
            }
        }
        else
        {
            await context.Database.EnsureCreatedAsync();
        }

        async Task<User> EnsureUserAsync(string username, string password, string firstName, string lastName, UserRole role, string mobile, Guid? adminRoleId = null, Guid? superAdminId = null)
        {
            var existing = await context.Users.FirstOrDefaultAsync(u => u.Username == username);
            if (existing != null)
            {
                existing.Role = role;
                existing.FirstName = firstName;
                existing.LastName = lastName;
                existing.PasswordHash = BCrypt.Net.BCrypt.HashPassword(password);
                existing.MobileNumber = mobile;
                existing.AdminRoleId = adminRoleId;
                existing.SuperAdminId = superAdminId;
                existing.IsFirstLogin = false;
                return existing;
            }

            var created = new User
            {
                Id = Guid.NewGuid(),
                Username = username,
                Role = role,
                Balance = 0m,
                FirstName = firstName,
                LastName = lastName,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
                DateOfBirth = new DateOnly(1990, 1, 1),
                MobileNumber = mobile,
                IsFirstLogin = false,
                AdminRoleId = adminRoleId,
                SuperAdminId = superAdminId,
            };

            context.Users.Add(created);
            return created;
        }

        var superAdminAlpesh = await EnsureUserAsync("alpesh.patel", "Alpesh@123", "Alpesh", "Patel", UserRole.SuperAdmin, "+919100000010");
        var superAdminJayendra = await EnsureUserAsync("jayendra.patel", "Jayendra@123", "Jayendra", "Patel", UserRole.SuperAdmin, "+919100000011");

        await context.SaveChangesAsync();

        async Task MergeDuplicateAdminRolesByNameAsync()
        {
            var roles = await context.AdminRoles
                .OrderBy(r => r.CreatedAt)
                .ThenBy(r => r.Id)
                .ToListAsync();

            var duplicateGroups = roles
                .GroupBy(r => r.Name.Trim().ToLowerInvariant())
                .Where(g => g.Count() > 1)
                .ToList();

            if (duplicateGroups.Count == 0)
                return;

            foreach (var group in duplicateGroups)
            {
                var keep = group.First();
                var duplicateIds = group.Skip(1).Select(r => r.Id).ToList();

                var usersToReassign = await context.Users
                    .Where(u => u.AdminRoleId.HasValue && duplicateIds.Contains(u.AdminRoleId.Value))
                    .ToListAsync();

                foreach (var user in usersToReassign)
                {
                    user.AdminRoleId = keep.Id;
                }

                var existingPermissionIds = await context.RolePermissions
                    .Where(rp => rp.RoleId == keep.Id)
                    .Select(rp => rp.PermissionId)
                    .ToListAsync();

                var permissionIdSet = existingPermissionIds.ToHashSet();

                var duplicateRolePermissions = await context.RolePermissions
                    .Where(rp => duplicateIds.Contains(rp.RoleId))
                    .ToListAsync();

                foreach (var link in duplicateRolePermissions)
                {
                    if (!permissionIdSet.Contains(link.PermissionId))
                    {
                        link.RoleId = keep.Id;
                        permissionIdSet.Add(link.PermissionId);
                    }
                    else
                    {
                        context.RolePermissions.Remove(link);
                    }
                }

                var rolesToDelete = await context.AdminRoles
                    .Where(r => duplicateIds.Contains(r.Id))
                    .ToListAsync();

                context.AdminRoles.RemoveRange(rolesToDelete);
            }

            await context.SaveChangesAsync();
        }

        await MergeDuplicateAdminRolesByNameAsync();

        var legacyDashboardPermission = await context.Permissions.FirstOrDefaultAsync(p => p.Slug == "dashboard");
        var generalDashboardPermission = await context.Permissions.FirstOrDefaultAsync(p => p.Slug == PermissionSlugs.DashboardGeneral);
        if (legacyDashboardPermission != null)
        {
            if (generalDashboardPermission == null)
            {
                legacyDashboardPermission.Slug = PermissionSlugs.DashboardGeneral;
                legacyDashboardPermission.Name = "General Dashboard";
            }
            else
            {
                var legacyRolePermissions = await context.RolePermissions
                    .Where(rp => rp.PermissionId == legacyDashboardPermission.Id)
                    .ToListAsync();

                var existingLinks = await context.RolePermissions
                    .Where(rp => rp.PermissionId == generalDashboardPermission.Id)
                    .Select(rp => new { rp.RoleId, rp.PermissionId })
                    .ToListAsync();

                foreach (var link in legacyRolePermissions)
                {
                    var alreadyLinked = existingLinks.Any(e => e.RoleId == link.RoleId && e.PermissionId == generalDashboardPermission.Id);
                    if (alreadyLinked)
                    {
                        context.RolePermissions.Remove(link);
                    }
                    else
                    {
                        link.PermissionId = generalDashboardPermission.Id;
                    }
                }

                context.Permissions.Remove(legacyDashboardPermission);
            }

            await context.SaveChangesAsync();
        }

        var permissionSpecs = new[]
        {
            ("General Dashboard", PermissionSlugs.DashboardGeneral),
            ("Analytical Dashbo", PermissionSlugs.DashboardAnalytical),
            ("Leave Approvals", PermissionSlugs.LeaveApprovals),
            ("Leave Management", PermissionSlugs.LeaveManagement),
            ("Upcoming Leaves", PermissionSlugs.UpcomingLeaves),
            ("Voucher Management", PermissionSlugs.VoucherManagement),
            ("Voucher Categories", PermissionSlugs.VoucherCategories),
            ("User Management", PermissionSlugs.UserManagement),
            ("Site Management", PermissionSlugs.SiteManagement),
            ("Designation Management", PermissionSlugs.DesignationManagement),
            ("Role Management", PermissionSlugs.RoleManagement),
        };

        foreach (var (name, slug) in permissionSpecs)
        {
            if (!await context.Permissions.AnyAsync(p => p.Slug == slug))
            {
                context.Permissions.Add(new Permission
                {
                    Id = Guid.NewGuid(),
                    Name = name,
                    Slug = slug,
                });
            }
        }

        await context.SaveChangesAsync();

        async Task<AdminRole> EnsureAdminRoleAsync(string roleName, Guid creatorId)
        {
            var existingRole = await context.AdminRoles.FirstOrDefaultAsync(r =>
                r.Name.ToLower() == roleName.ToLower());
            if (existingRole != null)
                return existingRole;

            var createdRole = new AdminRole
            {
                Id = Guid.NewGuid(),
                Name = roleName,
                CreatedBy = creatorId,
                CreatedAt = DateTime.Now,
            };

            context.AdminRoles.Add(createdRole);
            return createdRole;
        }

        var hrRole = await EnsureAdminRoleAsync("HR-Admin", superAdminAlpesh.Id);
        var accountRole = await EnsureAdminRoleAsync("Account-Admin", superAdminAlpesh.Id);
        var pillerRole = await EnsureAdminRoleAsync("Piller", superAdminAlpesh.Id);
        var generalUserRole = await EnsureAdminRoleAsync("General User", superAdminAlpesh.Id);

        await context.SaveChangesAsync();

        async Task SetRolePermissionsAsync(Guid roleId, params string[] slugs)
        {
            var existingLinks = await context.RolePermissions.Where(rp => rp.RoleId == roleId).ToListAsync();
            context.RolePermissions.RemoveRange(existingLinks);

            var permissionIds = await context.Permissions
                .Where(p => slugs.Contains(p.Slug))
                .Select(p => p.Id)
                .ToListAsync();

            var links = permissionIds.Select(permissionId => new RolePermission
            {
                Id = Guid.NewGuid(),
                RoleId = roleId,
                PermissionId = permissionId,
            });

            context.RolePermissions.AddRange(links);
        }

        await SetRolePermissionsAsync(
            hrRole.Id,
            PermissionSlugs.LeaveApprovals,
            PermissionSlugs.LeaveManagement,
            PermissionSlugs.UpcomingLeaves,
            PermissionSlugs.UserManagement,
            PermissionSlugs.SiteManagement,
            PermissionSlugs.DesignationManagement);

        await SetRolePermissionsAsync(
            accountRole.Id,
            PermissionSlugs.VoucherManagement,
            PermissionSlugs.VoucherCategories);
        await SetRolePermissionsAsync(pillerRole.Id);
        await SetRolePermissionsAsync(generalUserRole.Id);

        await context.SaveChangesAsync();

        var hrAdmin = await EnsureUserAsync("radhika.patel", "Radhika@123", "Radhika", "Patel", UserRole.Admin, "+919100000020", hrRole.Id, superAdminAlpesh.Id);
        var accountAdmin = await EnsureUserAsync("vipul.pate", "Vipul@123", "Vipul", "Pate", UserRole.Admin, "+919100000021", accountRole.Id, superAdminAlpesh.Id);
        var accountAdminJayendra = await EnsureUserAsync("jignesh.patel", "Jignesh@123", "Jignesh", "Patel", UserRole.Admin, "+919100000022", accountRole.Id, superAdminJayendra.Id);

        var pillerViral = await EnsureUserAsync("viral.patel", "Viral@123", "Viral", "Patel", UserRole.Piller, "+919100000030", null, superAdminAlpesh.Id);
        var pillerTalshang = await EnsureUserAsync("talshang", "Talshang@123", "Talshang", "Patel", UserRole.Piller, "+919100000031", null, superAdminJayendra.Id);

        var generalUserAarav = await EnsureUserAsync("aarav.shah", "Aarav@123", "Aarav", "Shah", UserRole.GeneralUser, "+919100000040", null, superAdminAlpesh.Id);
        var generalUserKavya = await EnsureUserAsync("kavya.patel", "Kavya@123", "Kavya", "Patel", UserRole.GeneralUser, "+919100000041", null, superAdminJayendra.Id);
        var generalUserMihir = await EnsureUserAsync("mihir.desai", "Mihir@123", "Mihir", "Desai", UserRole.GeneralUser, "+919100000042", null, superAdminAlpesh.Id);

        hrAdmin.SuperAdminId = superAdminAlpesh.Id;
        accountAdmin.SuperAdminId = superAdminAlpesh.Id;
        accountAdminJayendra.SuperAdminId = superAdminJayendra.Id;
        pillerViral.SuperAdminId = superAdminAlpesh.Id;
        pillerTalshang.SuperAdminId = superAdminJayendra.Id;
        generalUserAarav.SuperAdminId = superAdminAlpesh.Id;
        generalUserKavya.SuperAdminId = superAdminJayendra.Id;
        generalUserMihir.SuperAdminId = superAdminAlpesh.Id;

        await context.SaveChangesAsync();

        var defaultLeaveTypes = new[]
        {
            new { Name = "Casual Leave", Description = "General planned leave", IsPaid = true, AnnualQuotaDays = (int?)12 },
            new { Name = "Sick Leave", Description = "Health-related leave", IsPaid = true, AnnualQuotaDays = (int?)8 },
            new { Name = "Unpaid Leave", Description = "Leave without pay", IsPaid = false, AnnualQuotaDays = (int?)null },
        };

        var leaveSeedNow = DateTime.Now;
        var leaveTypeEntities = new List<LeaveType>();
        foreach (var leaveType in defaultLeaveTypes)
        {
            var existingType = await context.LeaveTypes.FirstOrDefaultAsync(t => t.Name == leaveType.Name);
            if (existingType != null)
            {
                existingType.Description = leaveType.Description;
                existingType.IsPaid = leaveType.IsPaid;
                existingType.IsActive = true;
                existingType.AnnualQuotaDays = leaveType.AnnualQuotaDays;
                existingType.UpdatedAt = leaveSeedNow;
                leaveTypeEntities.Add(existingType);
                continue;
            }

            var createdType = new LeaveType
            {
                Id = Guid.NewGuid(),
                Name = leaveType.Name,
                Description = leaveType.Description,
                IsPaid = leaveType.IsPaid,
                IsActive = true,
                AnnualQuotaDays = leaveType.AnnualQuotaDays,
                CreatedAt = leaveSeedNow,
                UpdatedAt = leaveSeedNow,
            };

            context.LeaveTypes.Add(createdType);
            leaveTypeEntities.Add(createdType);
        }

        await context.SaveChangesAsync();

        var managedUsers = new[]
        {
            hrAdmin,
            accountAdmin,
            accountAdminJayendra,
            pillerViral,
            pillerTalshang,
            generalUserAarav,
            generalUserKavya,
            generalUserMihir,
        };

        foreach (var user in managedUsers)
        {
            foreach (var leaveType in leaveTypeEntities)
            {
                var assignmentExists = await context.LeaveTypeAssignments
                    .AnyAsync(a => a.UserId == user.Id && a.LeaveTypeId == leaveType.Id);

                if (assignmentExists)
                    continue;

                context.LeaveTypeAssignments.Add(new LeaveTypeAssignment
                {
                    Id = Guid.NewGuid(),
                    UserId = user.Id,
                    LeaveTypeId = leaveType.Id,
                    AnnualQuotaDaysOverride = null,
                    CreatedAt = leaveSeedNow,
                    UpdatedAt = leaveSeedNow,
                });
            }
        }

        await context.SaveChangesAsync();

        var currentYear = DateTime.Now.Year;
        var defaultHolidays = new[]
        {
            new { Date = new DateOnly(currentYear, 1, 26), Name = "Republic Day" },
            new { Date = new DateOnly(currentYear, 8, 15), Name = "Independence Day" },
            new { Date = new DateOnly(currentYear, 10, 2), Name = "Gandhi Jayanti" },
        };

        foreach (var holiday in defaultHolidays)
        {
            var exists = await context.PublicHolidays.AnyAsync(h => h.Date == holiday.Date);
            if (exists)
                continue;

            context.PublicHolidays.Add(new PublicHoliday
            {
                Id = Guid.NewGuid(),
                Date = holiday.Date,
                Name = holiday.Name,
                Description = null,
                IsActive = true,
                CreatedAt = leaveSeedNow,
                UpdatedAt = leaveSeedNow,
            });
        }

        await context.SaveChangesAsync();

        // Seed default voucher categories if none exist
        if (!await context.VoucherCategories.AnyAsync())
        {
            var defaultCategories = new[]
            {
                "Fuel Expense",
                "Labour Expense",
                "Office Expense",
                "Overhead Expense",
                "Vehicle Expense",
                "Transportation",
                "Other Expense",
            };

            var now = DateTime.Now;
            for (var i = 0; i < defaultCategories.Length; i++)
            {
                context.VoucherCategories.Add(new VoucherCategoryConfig
                {
                    Id = Guid.NewGuid(),
                    Name = defaultCategories[i],
                    IsActive = true,
                    SortOrder = i,
                    CreatedAt = now,
                    UpdatedAt = now,
                });
            }
            await context.SaveChangesAsync();
        }

        await SeedDemoStatementDataAsync(
            superAdminAlpesh,
            hrAdmin,
            accountAdmin,
            pillerViral,
            pillerTalshang,
            generalUserAarav,
            generalUserKavya,
            generalUserMihir);

        async Task SeedDemoStatementDataAsync(User rootSuperAdmin, params User[] users)
        {
            var targetUserIds = users.Select(u => u.Id).ToList();
            if (targetUserIds.Count == 0)
                return;

            var demoVoucherIds = await context.Vouchers
                .Where(v => v.VoucherNumber.StartsWith("DEMO-"))
                .Select(v => v.Id)
                .ToListAsync();

            if (demoVoucherIds.Count > 0)
            {
                var demoVoucherTransactions = await context.BalanceTransactions
                    .Where(t => t.ReferenceType == "VoucherApproval" && t.ReferenceId.HasValue && demoVoucherIds.Contains(t.ReferenceId.Value))
                    .ToListAsync();

                var demoVouchers = await context.Vouchers
                    .Where(v => demoVoucherIds.Contains(v.Id))
                    .ToListAsync();

                context.BalanceTransactions.RemoveRange(demoVoucherTransactions);
                context.Vouchers.RemoveRange(demoVouchers);
            }

            var directDemoTransactions = await context.BalanceTransactions
                .Where(t => t.ReferenceType == "SeedDemo" && targetUserIds.Contains(t.UserId))
                .ToListAsync();

            if (directDemoTransactions.Count > 0)
            {
                context.BalanceTransactions.RemoveRange(directDemoTransactions);
            }

            await context.SaveChangesAsync();

            var categoryNames = await context.VoucherCategories
                .Where(c => c.IsActive)
                .OrderBy(c => c.SortOrder)
                .Select(c => c.Name)
                .ToListAsync();

            if (categoryNames.Count == 0)
            {
                categoryNames = new List<string>
                {
                    "Fuel Expense",
                    "Labour Expense",
                    "Office Expense",
                    "Overhead Expense",
                    "Vehicle Expense",
                    "Transportation",
                    "Other Expense",
                };
            }

            var today = DateTime.Today;

            for (var userIndex = 0; userIndex < users.Length; userIndex++)
            {
                var user = users[userIndex];
                decimal runningBalance = 0m;

                var firstCreditDate = today.AddDays(-34 + userIndex);
                var secondCreditDate = today.AddDays(-20 + userIndex);

                var firstCredit = 40000m + (userIndex * 5000m);
                var secondCredit = 18000m + (userIndex * 2500m);

                runningBalance += firstCredit;
                context.BalanceTransactions.Add(new BalanceTransaction
                {
                    Id = Guid.NewGuid(),
                    UserId = user.Id,
                    Type = BalanceTransactionType.Credit,
                    Amount = firstCredit,
                    BalanceAfter = runningBalance,
                    EntryDate = firstCreditDate,
                    Description = "Seeded opening credit",
                    ReferenceType = "SeedDemo",
                    CreatedById = rootSuperAdmin.Id,
                });

                runningBalance += secondCredit;
                context.BalanceTransactions.Add(new BalanceTransaction
                {
                    Id = Guid.NewGuid(),
                    UserId = user.Id,
                    Type = BalanceTransactionType.Credit,
                    Amount = secondCredit,
                    BalanceAfter = runningBalance,
                    EntryDate = secondCreditDate,
                    Description = "Seeded mid-cycle credit",
                    ReferenceType = "SeedDemo",
                    CreatedById = rootSuperAdmin.Id,
                });

                var debitAmounts = new[]
                {
                    2350m + (userIndex * 120m),
                    4180m + (userIndex * 150m),
                    1890m + (userIndex * 100m),
                    5120m + (userIndex * 180m),
                    2740m + (userIndex * 110m),
                };

                var siteNames = new[]
                {
                    "Head Office",
                    "Plant A",
                    "Warehouse",
                    "Site East",
                    "Site West",
                };

                for (var voucherIndex = 0; voucherIndex < debitAmounts.Length; voucherIndex++)
                {
                    var amount = debitAmounts[voucherIndex];
                    var voucherDate = today.AddDays(-31 + (voucherIndex * 6) + userIndex);
                    var approvalDate = voucherDate.AddDays(1);
                    var voucherId = Guid.NewGuid();
                    var demoVoucherNumber = $"DEMO-{userIndex + 1:00}-{voucherIndex + 1:000}";

                    context.Vouchers.Add(new Voucher
                    {
                        Id = voucherId,
                        VoucherNumber = demoVoucherNumber,
                        Title = $"Demo expense {voucherIndex + 1}",
                        Description = $"Demo seeded voucher for {user.FirstName} {user.LastName} entry {voucherIndex + 1}",
                        Amount = amount,
                        VoucherDate = voucherDate,
                        Category = categoryNames[voucherIndex % categoryNames.Count],
                        SiteName = siteNames[voucherIndex % siteNames.Length],
                        Status = VoucherStatus.Approved,
                        CurrentApprovalLevel = LeaveApprovalLevel.SuperAdmin,
                        CreatedById = user.Id,
                        CreatedAt = voucherDate,
                        UpdatedAt = approvalDate,
                        AdminApprovedById = rootSuperAdmin.Id,
                        AdminApprovedAt = approvalDate,
                        SuperAdminApprovedById = rootSuperAdmin.Id,
                        SuperAdminApprovedAt = approvalDate,
                        OwningSuperAdminId = user.Role == UserRole.SuperAdmin
                            ? user.Id
                            : (user.SuperAdminId ?? rootSuperAdmin.Id),
                    });

                    runningBalance -= amount;
                    context.BalanceTransactions.Add(new BalanceTransaction
                    {
                        Id = Guid.NewGuid(),
                        UserId = user.Id,
                        Type = BalanceTransactionType.Debit,
                        Amount = amount,
                        BalanceAfter = runningBalance,
                        EntryDate = approvalDate,
                        Description = "Voucher expense",
                        ReferenceType = "VoucherApproval",
                        ReferenceId = voucherId,
                        CreatedById = rootSuperAdmin.Id,
                    });
                }

                user.Balance = runningBalance;
            }

            await context.SaveChangesAsync();
        }
    }
}
