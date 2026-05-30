namespace RyveSwift.Api.Entities;

public class Quote
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? UserId { get; set; }
    public string OriginCountry { get; set; } = "";
    public string DestinationCountry { get; set; } = "";
    public string OriginCity { get; set; } = "";
    public string? OriginPostalCode { get; set; }
    public string DestinationCity { get; set; } = "";
    public string? DestinationPostalCode { get; set; }
    public string ProductCode { get; set; } = "P";
    public decimal WeightKg { get; set; }
    public decimal LengthCm { get; set; }
    public decimal WidthCm { get; set; }
    public decimal HeightCm { get; set; }
    public decimal DhlBaseRate { get; set; }
    public string DhlCurrency { get; set; } = "CAD";
    public decimal MarkupPercent { get; set; }
    public decimal PlatformFee { get; set; } = 0;
    public decimal TotalAmount { get; set; }
    public string Currency { get; set; } = "CAD";
    public string? RawDhlRateResponse { get; set; }
    public int Pieces { get; set; } = 1;
    public string? CustomsCategory { get; set; }
    public decimal? CustomsDeclaredValue { get; set; }
    public string? CustomsCurrency { get; set; }
    public string? CustomsReason { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public User? User { get; set; }
}
