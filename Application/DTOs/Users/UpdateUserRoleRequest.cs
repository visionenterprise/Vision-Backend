using System.ComponentModel.DataAnnotations;
using vision_backend.Domain.Enums;

namespace vision_backend.Application.DTOs.Users;

public class UpdateUserRoleRequest
{
    [Required]
    public UserRole Role { get; set; }

    public Guid? AdminRoleId { get; set; }
}