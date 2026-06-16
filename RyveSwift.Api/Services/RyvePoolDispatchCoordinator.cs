using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RyveSwift.Api.Data;
using RyveSwift.Api.Dtos;
using RyveSwift.Api.Entities;

namespace RyveSwift.Api.Services;

public class RyvePoolDispatchCoordinator
{
    public const string DispatchTimingImmediate = "immediate";
    public const string DispatchTimingScheduled = "scheduled";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly AppDbContext _db;
    private readonly RyvePoolService _ryvePool;
    private readonly ILogger<RyvePoolDispatchCoordinator> _logger;

    public RyvePoolDispatchCoordinator(
        AppDbContext db,
        RyvePoolService ryvePool,
        ILogger<RyvePoolDispatchCoordinator> logger)
    {
        _db = db;
        _ryvePool = ryvePool;
        _logger = logger;
    }

    public static string NormalizeDispatchTiming(string? dispatchTiming)
    {
        var value = string.IsNullOrWhiteSpace(dispatchTiming)
            ? DispatchTimingImmediate
            : dispatchTiming.Trim().ToLowerInvariant();

        return value switch
        {
            "now" or "immediate" => DispatchTimingImmediate,
            "later" or "scheduled" => DispatchTimingScheduled,
            _ => throw new RyvePoolException(
                "unsupported_dispatch_timing",
                "Dispatch timing must be immediate or scheduled.",
                StatusCodes.Status422UnprocessableEntity)
        };
    }

    public async Task<RyvePoolDelivery?> CreateShipmentDeliveryAsync(
        Quote quote,
        Shipment shipment,
        Guid userId,
        CancellationToken ct)
    {
        if (!quote.RyvePoolDeliverySelected)
            return null;

        var existing = await _db.RyvePoolDeliveries
            .FirstOrDefaultAsync(d => d.ShipmentId == shipment.Id || d.QuoteId == quote.Id, ct);
        if (existing is not null)
            return existing;

        var cfg = _ryvePool.GetRuntimeConfig();
        var dispatchTiming = NormalizeDispatchTiming(quote.RyvePoolDeliveryDispatchTiming);
        var scheduledFor = quote.RyvePoolDeliveryScheduledForUtc;
        var shouldDispatchNow = dispatchTiming == DispatchTimingImmediate ||
            !scheduledFor.HasValue ||
            scheduledFor.Value <= DateTime.UtcNow;

        var metadata = new Dictionary<string, string>
        {
            ["source"] = "ryvesend_shipment_addon",
            ["quoteId"] = quote.Id.ToString(),
            ["shipmentId"] = shipment.Id.ToString()
        };
        if (!string.IsNullOrWhiteSpace(shipment.TrackingNumber))
            metadata["dhlTrackingNumber"] = shipment.TrackingNumber!;
        if (!string.IsNullOrWhiteSpace(quote.RyvePoolDhlPointId))
            metadata["dhlPointId"] = quote.RyvePoolDhlPointId!;
        if (!string.IsNullOrWhiteSpace(quote.RyvePoolDhlPointName))
            metadata["dhlPointName"] = quote.RyvePoolDhlPointName!;

        var delivery = new RyvePoolDelivery
        {
            UserId = userId,
            QuoteId = quote.Id,
            ShipmentId = shipment.Id,
            Environment = cfg.Environment,
            ExternalOrderId = $"ryvesend-shipment-{shipment.Id:N}",
            MerchantReference = shipment.TrackingNumber ?? shipment.Id.ToString(),
            ExternalBranchId = quote.RyvePoolExternalBranchId ?? cfg.DefaultExternalBranchId,
            Status = shouldDispatchNow ? "dispatch_pending" : "scheduled",
            RegionCode = quote.RyvePoolRegionCode ?? cfg.DefaultRegionCode,
            DispatchModeRequested = quote.RyvePoolDispatchMode ?? cfg.DefaultDispatchMode,
            DispatchTiming = dispatchTiming,
            ScheduledForUtc = scheduledFor,
            PaymentType = "prepaid",
            PackageType = quote.RyvePoolPackageType ?? cfg.DefaultPackageType,
            ParcelWeightKg = quote.RyvePoolParcelWeightKg ?? quote.WeightKg,
            DriverInstructions = quote.RyvePoolDriverInstructions,
            Currency = quote.RyvePoolDeliveryCurrency ?? quote.Currency,
            DeliveryFeeMinor = quote.RyvePoolDeliveryFeeMinor,
            DhlPointId = quote.RyvePoolDhlPointId,
            DhlPointName = quote.RyvePoolDhlPointName,
            PickupName = quote.RyvePoolPickupName ?? "",
            PickupPhone = quote.RyvePoolPickupPhone ?? "",
            PickupAddress = quote.RyvePoolPickupAddress,
            PickupLandmark = quote.RyvePoolPickupLandmark,
            PickupLat = quote.RyvePoolPickupLat,
            PickupLng = quote.RyvePoolPickupLng,
            DropoffName = quote.RyvePoolDropoffName ?? "",
            DropoffPhone = quote.RyvePoolDropoffPhone ?? "",
            DropoffEmail = quote.RyvePoolDropoffEmail,
            DropoffAddress = quote.RyvePoolDropoffAddress,
            DropoffLandmark = quote.RyvePoolDropoffLandmark,
            DropoffLat = quote.RyvePoolDropoffLat,
            DropoffLng = quote.RyvePoolDropoffLng,
            MetadataJson = JsonSerializer.Serialize(metadata, JsonOpts),
            RawQuoteResponse = quote.RyvePoolDeliveryQuoteRawResponse,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _db.RyvePoolDeliveries.Add(delivery);
        quote.RyvePoolDeliveryStatus = delivery.Status;
        AddShipmentEvent(shipment.Id, "RyvePoolDeliveryCreated",
            shouldDispatchNow
                ? "RyvePool delivery add-on created and queued for dispatch."
                : $"RyvePool delivery add-on scheduled for {scheduledFor:O}.");

        await _db.SaveChangesAsync(ct);

        if (shouldDispatchNow)
        {
            await TryDispatchStoredDeliveryAsync(delivery, ct);
            quote.RyvePoolDeliveryStatus = delivery.Status;
            await _db.SaveChangesAsync(ct);
        }

        return delivery;
    }

    public async Task<bool> TryDispatchStoredDeliveryAsync(RyvePoolDelivery delivery, CancellationToken ct)
    {
        try
        {
            await DispatchStoredDeliveryAsync(delivery, ct);
            return true;
        }
        catch (RyvePoolException ex)
        {
            delivery.Status = "dispatch_failed";
            delivery.LastDispatchError = ex.Message;
            delivery.UpdatedAt = DateTime.UtcNow;
            await UpdateQuoteStatusAsync(delivery, ct);
            AddShipmentEvent(delivery.ShipmentId, "RyvePoolDispatchFailed", ex.Message);
            await _db.SaveChangesAsync(ct);
            _logger.LogWarning(ex, "RyvePool dispatch failed for local delivery {DeliveryId}", delivery.Id);
            return false;
        }
    }

    public async Task DispatchStoredDeliveryAsync(RyvePoolDelivery delivery, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(delivery.RyvePoolDispatchId))
            return;

        var cfg = _ryvePool.GetRuntimeConfig();
        if (!delivery.Environment.Equals(cfg.Environment, StringComparison.OrdinalIgnoreCase))
        {
            throw new RyvePoolException(
                "ryvepool_environment_mismatch",
                "This delivery belongs to a different RyvePool environment than the active backend configuration.",
                StatusCodes.Status409Conflict);
        }

        delivery.DispatchAttemptCount += 1;
        delivery.LastDispatchAttemptAt = DateTime.UtcNow;
        delivery.Status = "dispatching";
        delivery.UpdatedAt = DateTime.UtcNow;

        var apiRequest = new RyvePoolDispatchApiRequest
        {
            ExternalOrderId = delivery.ExternalOrderId,
            MerchantReference = delivery.MerchantReference,
            ExternalBranchId = delivery.ExternalBranchId ?? cfg.DefaultExternalBranchId,
            DispatchMode = delivery.DispatchModeRequested ?? cfg.DefaultDispatchMode,
            Pickup = MapPickup(delivery),
            Dropoff = MapDropoff(delivery),
            PaymentType = delivery.PaymentType,
            CodAmountMinor = delivery.CodAmountMinor,
            PackageType = delivery.PackageType,
            ParcelWeightKg = delivery.ParcelWeightKg,
            DriverInstructions = delivery.DriverInstructions,
            Metadata = DeserializeMetadata(delivery.MetadataJson)
        };

        var (dispatch, raw) = await _ryvePool.CreateDispatchAsync(apiRequest, ct);
        delivery.RawCreateResponse = raw;
        delivery.RawLatestResponse = raw;
        delivery.LastDispatchError = null;
        ApplyDispatchResponse(delivery, dispatch);
        await UpdateQuoteStatusAsync(delivery, ct);
        AddShipmentEvent(delivery.ShipmentId, "RyvePoolDispatchCreated",
            $"RyvePool dispatch created: {delivery.RyvePoolDispatchId ?? delivery.ExternalOrderId}.");

        await _db.SaveChangesAsync(ct);
    }

    private static RyvePoolDispatchAddressApiRequest MapPickup(RyvePoolDelivery delivery) => new()
    {
        Name = delivery.PickupName,
        Phone = delivery.PickupPhone,
        Address = delivery.PickupAddress,
        Landmark = delivery.PickupLandmark,
        Lat = delivery.PickupLat,
        Lng = delivery.PickupLng
    };

    private static RyvePoolDispatchAddressApiRequest MapDropoff(RyvePoolDelivery delivery) => new()
    {
        Name = delivery.DropoffName,
        Phone = delivery.DropoffPhone,
        Address = delivery.DropoffAddress,
        Landmark = delivery.DropoffLandmark,
        Lat = delivery.DropoffLat,
        Lng = delivery.DropoffLng,
        Email = delivery.DropoffEmail
    };

    private static Dictionary<string, string>? DeserializeMetadata(string? metadataJson)
    {
        if (string.IsNullOrWhiteSpace(metadataJson))
            return null;

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(metadataJson, JsonOpts);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private void AddShipmentEvent(Guid? shipmentId, string eventType, string description)
    {
        if (!shipmentId.HasValue)
            return;

        _db.ShipmentEvents.Add(new ShipmentEvent
        {
            ShipmentId = shipmentId.Value,
            EventType = eventType,
            Description = description
        });
    }

    private async Task UpdateQuoteStatusAsync(RyvePoolDelivery delivery, CancellationToken ct)
    {
        if (!delivery.QuoteId.HasValue)
            return;

        var quote = await _db.Quotes.FirstOrDefaultAsync(q => q.Id == delivery.QuoteId.Value, ct);
        if (quote is not null)
            quote.RyvePoolDeliveryStatus = delivery.Status;
    }

    private static void ApplyDispatchResponse(RyvePoolDelivery delivery, RyvePoolDispatchApiResponse dispatch)
    {
        if (!string.IsNullOrWhiteSpace(dispatch.Id))
            delivery.RyvePoolDispatchId = dispatch.Id;
        if (!string.IsNullOrWhiteSpace(dispatch.ExternalOrderId))
            delivery.ExternalOrderId = dispatch.ExternalOrderId;
        delivery.MerchantReference = dispatch.MerchantReference ?? delivery.MerchantReference;
        delivery.Status = string.IsNullOrWhiteSpace(dispatch.Status) ? "created" : dispatch.Status;
        delivery.TrackingUrl = dispatch.TrackingUrl ?? delivery.TrackingUrl;
        delivery.ExternalBranchId = dispatch.ExternalBranchId ?? delivery.ExternalBranchId;
        delivery.RegionCode = dispatch.RegionCode ?? delivery.RegionCode;
        delivery.Timezone = dispatch.Timezone ?? delivery.Timezone;
        delivery.DispatchModeRequested = dispatch.DispatchModeRequested ?? delivery.DispatchModeRequested;
        delivery.DispatchModeUsed = dispatch.DispatchModeUsed ?? delivery.DispatchModeUsed;
        delivery.DriverPool = dispatch.DriverPool ?? delivery.DriverPool;
        delivery.PaymentType = dispatch.PaymentType ?? delivery.PaymentType;
        delivery.CodAmountMinor = dispatch.CodAmountMinor;
        delivery.Currency = dispatch.Currency ?? delivery.Currency;
        delivery.ShortCode = dispatch.ShortCode ?? delivery.ShortCode;
        delivery.CreatedAt = dispatch.CreatedAt ?? delivery.CreatedAt;
        delivery.UpdatedAt = dispatch.UpdatedAt ?? DateTime.UtcNow;
        delivery.PickedUpAt = dispatch.PickedUpAt ?? delivery.PickedUpAt;
        delivery.DeliveredAt = dispatch.DeliveredAt ?? delivery.DeliveredAt;
        delivery.CancelledAt = dispatch.CancelledAt ?? delivery.CancelledAt;
        delivery.FailedAt = dispatch.FailedAt ?? delivery.FailedAt;

        if (dispatch.Accounting is not null)
        {
            delivery.DeliveryFeeMinor = dispatch.Accounting.DeliveryFeeMinor;
            delivery.PlatformFeeMinor = dispatch.Accounting.PlatformFeeMinor;
            delivery.RyvePoolCommissionMinor = dispatch.Accounting.RyvePoolCommissionMinor;
            delivery.DriverPayoutMinor = dispatch.Accounting.DriverPayoutMinor;
            delivery.NotificationFeeMinor = dispatch.Accounting.NotificationFeeMinor;
            delivery.TaxMinor = dispatch.Accounting.TaxMinor;
            delivery.PaymentProcessingFeeMinor = dispatch.Accounting.PaymentProcessingFeeMinor;
            delivery.RefundAdjustmentMinor = dispatch.Accounting.RefundAdjustmentMinor;
            delivery.SettlementStatus = dispatch.Accounting.SettlementStatus;
        }

        if (dispatch.CancellationPolicy is not null)
        {
            delivery.CancellationWindowMinutes = dispatch.CancellationPolicy.WindowMinutes;
            delivery.CancellableUntil = dispatch.CancellationPolicy.CancellableUntil;
            delivery.CanCancel = dispatch.CancellationPolicy.CanCancel;
        }
    }
}
