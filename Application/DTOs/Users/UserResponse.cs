using vision_backend.Domain.Enums;

namespace vision_backend.Application.DTOs.Users;

public class UserResponse
{
    public Guid Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public UserRole Role { get; set; }
    public decimal Balance { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
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
    public string? DesignationName { get; set; }
    public DateOnly? JoiningDate { get; set; }
    public string? PfNo { get; set; }
    public string? EsicNo { get; set; }
    public string? BankName { get; set; }
    public string? IfscCode { get; set; }
    public string? AccountNumber { get; set; }
    public string? BankBranch { get; set; }
    public Guid? AdminRoleId { get; set; }
    public string? AdminRoleName { get; set; }
    public Guid? SuperAdminId { get; set; }
    public List<string> ModuleAccess { get; set; } = new();
}
