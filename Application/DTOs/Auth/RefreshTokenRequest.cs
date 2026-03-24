using System.ComponentModel.DataAnnotations;

namespace vision_backend.Application.DTOs.Auth;

public class RefreshTokenRequest
{
    [Required]
    public string RefreshToken { get; set; } = string.Empty;
}
