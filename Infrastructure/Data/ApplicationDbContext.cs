using Microsoft.EntityFrameworkCore;
using vision_backend.Domain.Entities;

namespace vision_backend.Infrastructure.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public DbSet<User> Users { get; set; } = null!;
        public DbSet<RefreshToken> RefreshTokens { get; set; } = null!;
        public DbSet<Voucher> Vouchers { get; set; } = null!;
        public DbSet<LeaveRequest> LeaveRequests { get; set; } = null!;
        public DbSet<LeaveType> LeaveTypes { get; set; } = null!;
        public DbSet<LeaveTypeAssignment> LeaveTypeAssignments { get; set; } = null!;
        public DbSet<PublicHoliday> PublicHolidays { get; set; } = null!;
        public DbSet<Site> Sites { get; set; } = null!;
        public DbSet<VoucherCategoryConfig> VoucherCategories { get; set; } = null!;
        public DbSet<Designation> Designations { get; set; } = null!;
        public DbSet<AdminRole> AdminRoles { get; set; } = null!;
        public DbSet<Permission> Permissions { get; set; } = null!;
        public DbSet<RolePermission> RolePermissions { get; set; } = null!;
        public DbSet<BalanceTransaction> BalanceTransactions { get; set; } = null!;
        public DbSet<Notification> Notifications { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<User>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => e.Username).IsUnique();
                entity.HasIndex(e => e.MobileNumber);

                entity.Property(e => e.Username)
                    .IsRequired()
                    .HasMaxLength(50);
                entity.Property(e => e.FirstName)
                    .IsRequired()
                    .HasMaxLength(100);
                entity.Property(e => e.LastName)
                    .IsRequired()
                    .HasMaxLength(100);
                entity.Property(e => e.PasswordHash)
                    .IsRequired()
                    .HasMaxLength(200);
                entity.Property(e => e.MobileNumber)
                    .IsRequired()
                    .HasMaxLength(20);
                entity.Property(e => e.Balance)
                    .HasPrecision(18, 2);
                entity.Property(e => e.DateOfBirth)
                    .HasColumnType("date");
                entity.Property(e => e.ProfilePictureUrl)
                    .HasMaxLength(500);
                entity.Property(e => e.AdminRoleId);
                entity.Property(e => e.SuperAdminId);

                // Additional employee detail fields
                entity.Property(e => e.Address).HasMaxLength(500);
                entity.Property(e => e.EmergencyContactNo).HasMaxLength(20);
                entity.Property(e => e.AadharCardNo).HasMaxLength(12);
                entity.Property(e => e.PanCardNo).HasMaxLength(10);
                entity.Property(e => e.BloodGroup).HasMaxLength(5);
                entity.Property(e => e.JoiningDate).HasColumnType("date");
                entity.Property(e => e.PfNo).HasMaxLength(50);
                entity.Property(e => e.EsicNo).HasMaxLength(50);
                entity.Property(e => e.BankName).HasMaxLength(100);
                entity.Property(e => e.IfscCode).HasMaxLength(11);
                entity.Property(e => e.AccountNumber).HasMaxLength(30);
                entity.Property(e => e.BankBranch).HasMaxLength(100);

                entity.HasOne(e => e.Designation)
                    .WithMany()
                    .HasForeignKey(e => e.DesignationId)
                    .OnDelete(DeleteBehavior.SetNull);

                entity.HasOne(e => e.AdminRole)
                    .WithMany(r => r.Users)
                    .HasForeignKey(e => e.AdminRoleId)
                    .OnDelete(DeleteBehavior.SetNull);

                entity.HasOne(e => e.SuperAdmin)
                    .WithMany(s => s.OwnedPillers)
                    .HasForeignKey(e => e.SuperAdminId)
                    .OnDelete(DeleteBehavior.SetNull);
            });

            modelBuilder.Entity<RefreshToken>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => e.Token).IsUnique();

                entity.Property(e => e.Token)
                    .IsRequired()
                    .HasMaxLength(200);
                entity.Property(e => e.CreatedAt)
                    .IsRequired();
                entity.Property(e => e.ExpiresAt)
                    .IsRequired();

                entity.HasOne(e => e.User)
                    .WithMany(u => u.RefreshTokens)
                    .HasForeignKey(e => e.UserId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<Voucher>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => e.VoucherNumber).IsUnique();

                entity.Property(e => e.VoucherNumber)
                    .IsRequired()
                    .HasMaxLength(20);
                entity.Property(e => e.Title)
                    .IsRequired()
                    .HasMaxLength(200);
                entity.Property(e => e.Description)
                    .HasMaxLength(1000);
                entity.Property(e => e.Amount)
                    .HasPrecision(18, 2);
                entity.Property(e => e.Category)
                    .IsRequired()
                    .HasMaxLength(100);
                entity.Property(e => e.ReceiptUrls)
                    .HasColumnType("text[]")
                    .IsRequired(false);
                entity.Property(e => e.CurrentApprovalLevel)
                    .HasConversion<int>();

                entity.Property(e => e.RejectionReason)
                    .HasMaxLength(500);

                entity.HasOne(e => e.CreatedBy)
                    .WithMany(u => u.Vouchers)
                    .HasForeignKey(e => e.CreatedById)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(e => e.AdminApprovedBy)
                    .WithMany()
                    .HasForeignKey(e => e.AdminApprovedById)
                    .OnDelete(DeleteBehavior.SetNull);

                entity.HasOne(e => e.SuperAdminApprovedBy)
                    .WithMany()
                    .HasForeignKey(e => e.SuperAdminApprovedById)
                    .OnDelete(DeleteBehavior.SetNull);

                entity.HasOne(e => e.RejectedBy)
                    .WithMany()
                    .HasForeignKey(e => e.RejectedById)
                    .OnDelete(DeleteBehavior.SetNull);

                entity.HasOne(e => e.TargetPiller)
                    .WithMany()
                    .HasForeignKey(e => e.TargetPillerId)
                    .OnDelete(DeleteBehavior.SetNull);

                entity.HasOne(e => e.PillerApprovedBy)
                    .WithMany()
                    .HasForeignKey(e => e.PillerApprovedById)
                    .OnDelete(DeleteBehavior.SetNull);

                entity.HasOne(e => e.OwningSuperAdmin)
                    .WithMany()
                    .HasForeignKey(e => e.OwningSuperAdminId)
                    .OnDelete(DeleteBehavior.SetNull);
            });

            modelBuilder.Entity<LeaveRequest>(entity =>
            {
                entity.HasKey(e => e.Id);
                
                entity.Property(e => e.Reason)
                    .IsRequired()
                    .HasMaxLength(500);

                entity.Property(e => e.StartDate)
                    .HasColumnType("date");

                entity.Property(e => e.EndDate)
                    .HasColumnType("date");

                entity.Property(e => e.RejectionReason)
                    .HasMaxLength(500);

                entity.Property(e => e.LeaveTypeName)
                    .HasMaxLength(100);

                entity.Property(e => e.LeaveYear)
                    .IsRequired();

                // Relationships
                entity.HasOne(e => e.User)
                    .WithMany(u => u.LeaveRequests)
                    .HasForeignKey(e => e.UserId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(e => e.LeaveType)
                    .WithMany(t => t.LeaveRequests)
                    .HasForeignKey(e => e.LeaveTypeId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(e => e.PillerApprovedBy)
                    .WithMany()
                    .HasForeignKey(e => e.PillerApprovedById)
                    .OnDelete(DeleteBehavior.SetNull);

                entity.HasOne(e => e.AdminApprovedBy)
                    .WithMany()
                    .HasForeignKey(e => e.AdminApprovedById)
                    .OnDelete(DeleteBehavior.SetNull);

                entity.HasOne(e => e.SuperAdminApprovedBy)
                    .WithMany()
                    .HasForeignKey(e => e.SuperAdminApprovedById)
                    .OnDelete(DeleteBehavior.SetNull);

                entity.HasOne(e => e.RejectedBy)
                    .WithMany()
                    .HasForeignKey(e => e.RejectedById)
                    .OnDelete(DeleteBehavior.SetNull);

                entity.HasOne(e => e.TargetPiller)
                    .WithMany()
                    .HasForeignKey(e => e.TargetPillerId)
                    .OnDelete(DeleteBehavior.SetNull);

                entity.HasOne(e => e.OwningSuperAdmin)
                    .WithMany()
                    .HasForeignKey(e => e.OwningSuperAdminId)
                    .OnDelete(DeleteBehavior.SetNull);
            });

            modelBuilder.Entity<LeaveType>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => e.Name).IsUnique();

                entity.Property(e => e.Name)
                    .IsRequired()
                    .HasMaxLength(100);

                entity.Property(e => e.Description)
                    .HasMaxLength(500);
            });

            modelBuilder.Entity<LeaveTypeAssignment>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => new { e.LeaveTypeId, e.UserId }).IsUnique();

                entity.HasOne(e => e.LeaveType)
                    .WithMany(t => t.Assignments)
                    .HasForeignKey(e => e.LeaveTypeId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(e => e.User)
                    .WithMany(u => u.LeaveTypeAssignments)
                    .HasForeignKey(e => e.UserId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<PublicHoliday>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => e.Date).IsUnique();

                entity.Property(e => e.Date)
                    .HasColumnType("date");

                entity.Property(e => e.Name)
                    .IsRequired()
                    .HasMaxLength(120);

                entity.Property(e => e.Description)
                    .HasMaxLength(500);
            });

            modelBuilder.Entity<BalanceTransaction>(entity =>
            {
                entity.HasKey(e => e.Id);

                entity.Property(e => e.Amount)
                    .HasPrecision(18, 2);

                entity.Property(e => e.BalanceAfter)
                    .HasPrecision(18, 2);

                entity.Property(e => e.Description)
                    .IsRequired()
                    .HasMaxLength(250);

                entity.Property(e => e.ReferenceType)
                    .HasMaxLength(50);

                entity.HasIndex(e => new { e.UserId, e.EntryDate });

                entity.HasOne(e => e.User)
                    .WithMany(u => u.BalanceTransactions)
                    .HasForeignKey(e => e.UserId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<Site>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => e.Name).IsUnique();

                entity.Property(e => e.Name)
                    .IsRequired()
                    .HasMaxLength(200);
            });

            modelBuilder.Entity<VoucherCategoryConfig>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => e.Name).IsUnique();

                entity.Property(e => e.Name)
                    .IsRequired()
                    .HasMaxLength(100);
            });

            modelBuilder.Entity<Designation>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => e.Name).IsUnique();

                entity.Property(e => e.Name)
                    .IsRequired()
                    .HasMaxLength(200);
            });

            modelBuilder.Entity<AdminRole>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => e.Name).IsUnique();

                entity.Property(e => e.Name)
                    .IsRequired()
                    .HasMaxLength(100);

                entity.HasOne(e => e.CreatedByUser)
                    .WithMany()
                    .HasForeignKey(e => e.CreatedBy)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<Permission>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => e.Slug).IsUnique();
                entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
                entity.Property(e => e.Slug).IsRequired().HasMaxLength(100);
            });

            modelBuilder.Entity<RolePermission>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => new { e.RoleId, e.PermissionId }).IsUnique();

                entity.HasOne(e => e.Role)
                    .WithMany(r => r.RolePermissions)
                    .HasForeignKey(e => e.RoleId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(e => e.Permission)
                    .WithMany(p => p.RolePermissions)
                    .HasForeignKey(e => e.PermissionId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<Notification>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => new { e.UserId, e.CreatedAt });
                entity.HasIndex(e => new { e.UserId, e.IsSeen });

                entity.Property(e => e.Type)
                    .IsRequired()
                    .HasMaxLength(80);

                entity.Property(e => e.Title)
                    .IsRequired()
                    .HasMaxLength(200);

                entity.Property(e => e.Message)
                    .IsRequired()
                    .HasMaxLength(1000);

                entity.Property(e => e.EntityType)
                    .HasMaxLength(50);

                entity.HasOne(e => e.User)
                    .WithMany(u => u.Notifications)
                    .HasForeignKey(e => e.UserId)
                    .OnDelete(DeleteBehavior.Cascade);
            });
        }
    }
}
