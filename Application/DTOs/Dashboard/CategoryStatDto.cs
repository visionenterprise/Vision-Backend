namespace vision_backend.Application.DTOs.Dashboard;

public class CategoryStatDto
{
    public string Category { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public int Count { get; set; }
}
