using System.ComponentModel.DataAnnotations;

namespace vision_backend.Application.DTOs.VoucherCategories;

public class UpdateVoucherCategoryRequest
{
    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public int SortOrder { get; set; } = 0;
}
