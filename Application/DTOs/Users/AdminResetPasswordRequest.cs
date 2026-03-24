using System.ComponentModel.DataAnnotations;

namespace vision_backend.Application.DTOs.Users;

public class AdminResetPasswordRequest
{
    [Required]
    [StringLength(100, MinimumLength = 8)]
    public string NewPassword { get; set; } = string.Empty;

    [Required]
    [Compare(nameof(NewPassword))]
    public string ConfirmPassword { get; set; } = string.Empty;
}
