using System.ComponentModel.DataAnnotations;

namespace vision_backend.Application.DTOs.Auth;

public class VerifyPasswordRequest
{
    [Required]
    public string Password { get; set; } = string.Empty;
}
