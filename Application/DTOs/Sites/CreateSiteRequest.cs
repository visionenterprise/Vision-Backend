using System.ComponentModel.DataAnnotations;

namespace vision_backend.Application.DTOs.Sites;

public class CreateSiteRequest
{
    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;
}
