using vision_backend.Domain.Enums;

namespace vision_backend.Domain.Entities;

public class User
{
    public Guid Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public UserRole Role { get; set; }
    public decimal Balance { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public DateOnly DateOfBirth { get; set; }
    public string MobileNumber { get; set; } = string.Empty;
    public bool IsFirstLogin { get; set; }
    public string? ProfilePictureUrl { get; set; }

    // Additional employee details
    public string? Address { get; set; }
    public string? EmergencyContactNo { get; set; }
    public string? AadharCardNo { get; set; }
    public string? PanCardNo { get; set; }
    public string? BloodGroup { get; set; }
    public Guid? DesignationId { get; set; }
    public Designation? Designation { get; set; }
    public DateOnly? JoiningDate { get; set; }
    public string? PfNo { get; set; }
    public string? EsicNo { get; set; }
    public string? BankName { get; set; }
    public string? IfscCode { get; set; }
    public string? AccountNumber { get; set; }
    public string? BankBranch { get; set; }
    public Guid? AdminRoleId { get; set; }
    public AdminRole? AdminRole { get; set; }
    public Guid? SuperAdminId { get; set; }
    public User? SuperAdmin { get; set; }
    public ICollection<User> OwnedPillers { get; set; } = new List<User>();

    public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();
    public ICollection<Voucher> Vouchers { get; set; } = new List<Voucher>();
    public ICollection<BalanceTransaction> BalanceTransactions { get; set; } = new List<BalanceTransaction>();
    public ICollection<Notification> Notifications { get; set; } = new List<Notification>();
    public ICollection<LeaveRequest> LeaveRequests { get; set; } = new List<LeaveRequest>();
    public ICollection<LeaveTypeAssignment> LeaveTypeAssignments { get; set; } = new List<LeaveTypeAssignment>();
}
