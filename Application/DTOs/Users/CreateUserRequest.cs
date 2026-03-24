using System.ComponentModel.DataAnnotations;
using vision_backend.Domain.Enums;

namespace vision_backend.Application.DTOs.Users;

public class CreateUserRequest
{
    [Required]
    [StringLength(50, MinimumLength = 3)]
    public string Username { get; set; } = string.Empty;

    [Required]
    public UserRole Role { get; set; }

    [Required]
    [StringLength(100)]
    public string FirstName { get; set; } = string.Empty;

    [Required]
    [StringLength(100)]
    public string LastName { get; set; } = string.Empty;

    [Required]
    public DateOnly DateOfBirth { get; set; }

    [Required]
    [RegularExpression("^\\+\\d{11,15}$")]
    public string MobileNumber { get; set; } = string.Empty;

    [Range(0, double.MaxValue)]
    public decimal? InitialBalance { get; set; }

    // Additional employee details (all optional)
    [StringLength(500)]
    public string? Address { get; set; }

    [RegularExpression("^\\+\\d{11,15}$")]
    public string? EmergencyContactNo { get; set; }

    [StringLength(12)]
    public string? AadharCardNo { get; set; }

    [StringLength(10)]
    public string? PanCardNo { get; set; }

    [StringLength(5)]
    public string? BloodGroup { get; set; }

    public Guid? DesignationId { get; set; }

    public DateOnly? JoiningDate { get; set; }

    [StringLength(50)]
    public string? PfNo { get; set; }

    [StringLength(50)]
    public string? EsicNo { get; set; }

    [StringLength(100)]
    public string? BankName { get; set; }

    [StringLength(11)]
    public string? IfscCode { get; set; }

    [StringLength(30)]
    public string? AccountNumber { get; set; }

    [StringLength(100)]
    public string? BankBranch { get; set; }

    public Guid? AdminRoleId { get; set; }
    public Guid? SuperAdminId { get; set; }
}
