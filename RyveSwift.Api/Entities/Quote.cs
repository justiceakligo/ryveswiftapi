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
    public string Incoterm { get; set; } = "DAP";
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
    public bool RyvePoolDeliverySelected { get; set; }
    public string? RyvePoolDeliveryStatus { get; set; }
    public string? RyvePoolDeliveryDispatchTiming { get; set; }
    public DateTime? RyvePoolDeliveryScheduledForUtc { get; set; }
    public long RyvePoolDeliveryFeeMinor { get; set; }
    public string? RyvePoolDeliveryCurrency { get; set; }
    public string? RyvePoolDeliveryQuoteRawResponse { get; set; }
    public string? RyvePoolPickupName { get; set; }
    public string? RyvePoolPickupPhone { get; set; }
    public string? RyvePoolPickupAddress { get; set; }
    public string? RyvePoolPickupLandmark { get; set; }
    public decimal? RyvePoolPickupLat { get; set; }
    public decimal? RyvePoolPickupLng { get; set; }
    public string? RyvePoolDropoffName { get; set; }
    public string? RyvePoolDropoffPhone { get; set; }
    public string? RyvePoolDropoffEmail { get; set; }
    public string? RyvePoolDropoffAddress { get; set; }
    public string? RyvePoolDropoffLandmark { get; set; }
    public decimal? RyvePoolDropoffLat { get; set; }
    public decimal? RyvePoolDropoffLng { get; set; }
    public string? RyvePoolDhlPointId { get; set; }
    public string? RyvePoolDhlPointName { get; set; }
    public string? RyvePoolRegionCode { get; set; }
    public string? RyvePoolExternalBranchId { get; set; }
    public string? RyvePoolDispatchMode { get; set; }
    public string? RyvePoolPackageType { get; set; }
    public decimal? RyvePoolParcelWeightKg { get; set; }
    public string? RyvePoolDriverInstructions { get; set; }
    public string? RyvePoolVehicleCategoryId { get; set; }
    public int Pieces { get; set; } = 1;
    public string? CustomsCategory { get; set; }
    public decimal? CustomsDeclaredValue { get; set; }
    public string? CustomsCurrency { get; set; }
    public string? CustomsReason { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public User? User { get; set; }
}
