namespace vision_backend.Domain.Entities;

public class Notification
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    public string Type { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;

    public string? EntityType { get; set; }
    public Guid? EntityId { get; set; }

    public bool IsSeen { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? SeenAt { get; set; }
}