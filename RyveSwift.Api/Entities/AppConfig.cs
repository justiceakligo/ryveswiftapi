namespace RyveSwift.Api.Entities;

public class AppConfig
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
    public string? Description { get; set; }
    public bool IsSecret { get; set; } = false;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
