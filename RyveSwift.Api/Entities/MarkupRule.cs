namespace RyveSwift.Api.Entities;

public class MarkupRule
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string? OriginCountry { get; set; }
    public string? DestinationCountry { get; set; }
    public decimal? MinWeightKg { get; set; }
    public decimal? MaxWeightKg { get; set; }
    public string? ProductCode { get; set; }
    public decimal MarkupPercent { get; set; }
    public decimal PlatformFee { get; set; } = 0;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
