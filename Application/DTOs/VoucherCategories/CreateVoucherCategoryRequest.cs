using System.ComponentModel.DataAnnotations;

namespace vision_backend.Application.DTOs.VoucherCategories;

public class CreateVoucherCategoryRequest
{
    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    public int SortOrder { get; set; } = 0;
}
