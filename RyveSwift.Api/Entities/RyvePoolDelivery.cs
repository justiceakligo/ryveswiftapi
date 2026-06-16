namespace RyveSwift.Api.Entities;

public class RyvePoolDelivery
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? UserId { get; set; }
    public Guid? QuoteId { get; set; }
    public Guid? ShipmentId { get; set; }
    public string Environment { get; set; } = "test";
    public string ExternalOrderId { get; set; } = "";
    public string? MerchantReference { get; set; }
    public string? ExternalBranchId { get; set; }
    public string? RyvePoolDispatchId { get; set; }
    public string Status { get; set; } = "created";
    public string? TrackingUrl { get; set; }
    public string RegionCode { get; set; } = "CA-ON";
    public string? Timezone { get; set; }
    public string? DispatchModeRequested { get; set; }
    public string? DispatchModeUsed { get; set; }
    public string? DriverPool { get; set; }
    public string PaymentType { get; set; } = "prepaid";
    public long CodAmountMinor { get; set; }
    public string PackageType { get; set; } = "parcel";
    public decimal? ParcelWeightKg { get; set; }
    public string? DriverInstructions { get; set; }
    public string Currency { get; set; } = "CAD";
    public long DeliveryFeeMinor { get; set; }
    public long PlatformFeeMinor { get; set; }
    public long RyvePoolCommissionMinor { get; set; }
    public long DriverPayoutMinor { get; set; }
    public long NotificationFeeMinor { get; set; }
    public long TaxMinor { get; set; }
    public long PaymentProcessingFeeMinor { get; set; }
    public long RefundAdjustmentMinor { get; set; }
    public string? SettlementStatus { get; set; }
    public int? CancellationWindowMinutes { get; set; }
    public DateTime? CancellableUntil { get; set; }
    public bool? CanCancel { get; set; }
    public string? ShortCode { get; set; }
    public string? DispatchTiming { get; set; }
    public DateTime? ScheduledForUtc { get; set; }
    public int DispatchAttemptCount { get; set; }
    public DateTime? LastDispatchAttemptAt { get; set; }
    public string? LastDispatchError { get; set; }
    public string? DhlPointId { get; set; }
    public string? DhlPointName { get; set; }

    public string PickupName { get; set; } = "";
    public string PickupPhone { get; set; } = "";
    public string? PickupAddress { get; set; }
    public string? PickupLandmark { get; set; }
    public decimal? PickupLat { get; set; }
    public decimal? PickupLng { get; set; }

    public string DropoffName { get; set; } = "";
    public string DropoffPhone { get; set; } = "";
    public string? DropoffEmail { get; set; }
    public string? DropoffAddress { get; set; }
    public string? DropoffLandmark { get; set; }
    public decimal? DropoffLat { get; set; }
    public decimal? DropoffLng { get; set; }

    public string? MetadataJson { get; set; }
    public string? RawQuoteResponse { get; set; }
    public string? RawCreateResponse { get; set; }
    public string? RawLatestResponse { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? PickedUpAt { get; set; }
    public DateTime? DeliveredAt { get; set; }
    public DateTime? CancelledAt { get; set; }
    public DateTime? FailedAt { get; set; }

    public User? User { get; set; }
    public Quote? Quote { get; set; }
    public Shipment? Shipment { get; set; }
    public ICollection<RyvePoolWebhookEvent> WebhookEvents { get; set; } = new List<RyvePoolWebhookEvent>();
}
