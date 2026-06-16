using System.Text.Json;
using System.Text.Json.Serialization;

namespace RyveSwift.Api.Dtos;

public record RyvePoolAddressInput(
    string Name,
    string Phone,
    string? Address,
    string? Landmark,
    decimal? Lat,
    decimal? Lng,
    string? Email = null);

public record RyvePoolQuoteRequest(
    decimal? PickupLat,
    decimal? PickupLng,
    decimal? DropoffLat,
    decimal? DropoffLng,
    string? PickupCity,
    string? DropoffCity,
    string? PackageType,
    decimal? WeightKg,
    string? RegionCode,
    string? VehicleCategoryId);

public record RyvePoolDeliveryCreateRequest(
    string ExternalOrderId,
    string? MerchantReference,
    string? ExternalBranchId,
    string? DispatchMode,
    RyvePoolAddressInput Pickup,
    RyvePoolAddressInput Dropoff,
    string? PaymentType,
    long? CodAmountMinor,
    string? PackageType,
    decimal? ParcelWeightKg,
    string? DriverInstructions,
    Dictionary<string, string>? Metadata);

public record RyvePoolRecipientUpdateRequest(
    string? RecipientName,
    string? RecipientPhone,
    string? RecipientEmail);

public record RyvePoolCancelRequest(string? Reason);

public record RyvePoolDeliveryResponse(
    Guid Id,
    Guid? QuoteId,
    Guid? ShipmentId,
    string Environment,
    string ExternalOrderId,
    string? RyvePoolDispatchId,
    string Status,
    string? TrackingUrl,
    string RegionCode,
    string? ExternalBranchId,
    string? DispatchModeUsed,
    string PaymentType,
    long CodAmountMinor,
    string PackageType,
    string Currency,
    long DeliveryFeeMinor,
    long PlatformFeeMinor,
    long RyvePoolCommissionMinor,
    long DriverPayoutMinor,
    bool? CanCancel,
    DateTime? CancellableUntil,
    string? DispatchTiming,
    DateTime? ScheduledForUtc,
    int DispatchAttemptCount,
    DateTime? LastDispatchAttemptAt,
    string? LastDispatchError,
    string? DhlPointId,
    string? DhlPointName,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public record RyvePoolConfigResponse(
    bool Enabled,
    string Environment,
    string BaseUrl,
    int TimeoutSeconds,
    string DefaultRegionCode,
    string? DefaultExternalBranchId,
    string DefaultDispatchMode,
    string DefaultPackageType,
    bool WebhookSignatureRequired,
    bool ScheduledDispatchEnabled,
    int ScheduledDispatchIntervalSeconds,
    RyvePoolCredentialStatus Test,
    RyvePoolCredentialStatus Production);

public record RyvePoolCredentialStatus(
    string? PublicKey,
    bool SecretConfigured,
    bool WebhookSecretConfigured);

public record RyvePoolConfigUpdateRequest(
    bool? Enabled,
    string? Environment,
    string? BaseUrl,
    int? TimeoutSeconds,
    string? DefaultRegionCode,
    string? DefaultExternalBranchId,
    string? DefaultDispatchMode,
    string? DefaultPackageType,
    bool? WebhookSignatureRequired,
    bool? ScheduledDispatchEnabled,
    int? ScheduledDispatchIntervalSeconds,
    string? TestPublicKey,
    string? TestSecretKey,
    string? TestWebhookSecret,
    string? ProductionPublicKey,
    string? ProductionSecretKey,
    string? ProductionWebhookSecret);

public record RyvePoolReportQuery(
    DateTime? From,
    DateTime? To,
    string? BranchId,
    string? RegionCode);

public record RyvePoolQuoteApiRequest
{
    [JsonPropertyName("pickup_lat")]
    public decimal? PickupLat { get; init; }

    [JsonPropertyName("pickup_lng")]
    public decimal? PickupLng { get; init; }

    [JsonPropertyName("dropoff_lat")]
    public decimal? DropoffLat { get; init; }

    [JsonPropertyName("dropoff_lng")]
    public decimal? DropoffLng { get; init; }

    [JsonPropertyName("pickup_city")]
    public string? PickupCity { get; init; }

    [JsonPropertyName("dropoff_city")]
    public string? DropoffCity { get; init; }

    [JsonPropertyName("package_type")]
    public string? PackageType { get; init; }

    [JsonPropertyName("weight_kg")]
    public decimal? WeightKg { get; init; }

    [JsonPropertyName("region_code")]
    public string? RegionCode { get; init; }

    [JsonPropertyName("vehicle_category_id")]
    public string? VehicleCategoryId { get; init; }
}

public record RyvePoolDispatchApiRequest
{
    [JsonPropertyName("external_order_id")]
    public string ExternalOrderId { get; init; } = "";

    [JsonPropertyName("merchant_reference")]
    public string? MerchantReference { get; init; }

    [JsonPropertyName("external_branch_id")]
    public string? ExternalBranchId { get; init; }

    [JsonPropertyName("dispatch_mode")]
    public string? DispatchMode { get; init; }

    [JsonPropertyName("pickup")]
    public RyvePoolDispatchAddressApiRequest Pickup { get; init; } = new();

    [JsonPropertyName("dropoff")]
    public RyvePoolDispatchAddressApiRequest Dropoff { get; init; } = new();

    [JsonPropertyName("payment_type")]
    public string PaymentType { get; init; } = "prepaid";

    [JsonPropertyName("cod_amount_minor")]
    public long CodAmountMinor { get; init; }

    [JsonPropertyName("package_type")]
    public string PackageType { get; init; } = "parcel";

    [JsonPropertyName("parcel_weight_kg")]
    public decimal? ParcelWeightKg { get; init; }

    [JsonPropertyName("driver_instructions")]
    public string? DriverInstructions { get; init; }

    [JsonPropertyName("metadata")]
    public Dictionary<string, string>? Metadata { get; init; }
}

public record RyvePoolDispatchAddressApiRequest
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("phone")]
    public string Phone { get; init; } = "";

    [JsonPropertyName("address")]
    public string? Address { get; init; }

    [JsonPropertyName("landmark")]
    public string? Landmark { get; init; }

    [JsonPropertyName("lat")]
    public decimal? Lat { get; init; }

    [JsonPropertyName("lng")]
    public decimal? Lng { get; init; }

    [JsonPropertyName("email")]
    public string? Email { get; init; }
}

public record RyvePoolRecipientUpdateApiRequest
{
    [JsonPropertyName("recipient_name")]
    public string? RecipientName { get; init; }

    [JsonPropertyName("recipient_phone")]
    public string? RecipientPhone { get; init; }

    [JsonPropertyName("recipient_email")]
    public string? RecipientEmail { get; init; }
}

public record RyvePoolCancelApiRequest
{
    [JsonPropertyName("reason")]
    public string? Reason { get; init; }
}

public record RyvePoolDispatchApiResponse
{
    public string Id { get; init; } = "";
    public string ExternalOrderId { get; init; } = "";
    public string? MerchantReference { get; init; }
    public string Status { get; init; } = "";
    public string? TrackingUrl { get; init; }
    public string? PartnerId { get; init; }
    public string? ApiKeyId { get; init; }
    public string? OrgId { get; init; }
    public string? BranchId { get; init; }
    public string? ExternalBranchId { get; init; }
    public string? RegionCode { get; init; }
    public string? Timezone { get; init; }
    public string? DispatchModeRequested { get; init; }
    public string? DispatchModeUsed { get; init; }
    public string? DriverPool { get; init; }
    public string? PaymentType { get; init; }
    public long CodAmountMinor { get; init; }
    public string? Currency { get; init; }
    public RyvePoolAccountingApiResponse? Accounting { get; init; }
    public RyvePoolCancellationPolicyApiResponse? CancellationPolicy { get; init; }
    public string? ShortCode { get; init; }
    public DateTime? CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }
    public DateTime? PickedUpAt { get; init; }
    public DateTime? DeliveredAt { get; init; }
    public DateTime? CancelledAt { get; init; }
    public DateTime? FailedAt { get; init; }
}

public record RyvePoolAccountingApiResponse
{
    public long DeliveryFeeMinor { get; init; }
    public long PlatformFeeMinor { get; init; }
    public long RyvePoolCommissionMinor { get; init; }
    public long DriverPayoutMinor { get; init; }
    public long NotificationFeeMinor { get; init; }
    public long TaxMinor { get; init; }
    public long PaymentProcessingFeeMinor { get; init; }
    public long RefundAdjustmentMinor { get; init; }
    public string? SettlementStatus { get; init; }
}

public record RyvePoolCancellationPolicyApiResponse
{
    public int WindowMinutes { get; init; }
    public DateTime? CancellableUntil { get; init; }
    public bool CanCancel { get; init; }
}

public record RyvePoolJsonEnvelope(JsonElement Json, string RawJson);
