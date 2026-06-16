using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RyveSwift.Api.Common;
using RyveSwift.Api.Data;
using RyveSwift.Api.Dtos;
using RyveSwift.Api.Entities;
using RyveSwift.Api.Services;

namespace RyveSwift.Api.Endpoints;

public static class RyvePoolDeliveryEndpoints
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public static void MapRyvePoolDeliveryEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/order-deliveries")
            .WithTags("RyvePool Order Deliveries");

        group.MapPost("/quotes", GetQuote)
            .WithName("RyvePoolGetOrderDeliveryQuote")
            .WithSummary("Get a RyvePool order delivery quote")
            .RequireRateLimiting("quotes")
            .AllowAnonymous();

        group.MapPost("/dispatches", CreateDispatch)
            .WithName("RyvePoolCreateOrderDeliveryDispatch")
            .WithSummary("Create a RyvePool order delivery dispatch")
            .RequireAuthorization();

        group.MapGet("/dispatches/{id:guid}", GetDispatch)
            .WithName("RyvePoolGetOrderDeliveryDispatch")
            .WithSummary("Get a stored RyvePool order delivery dispatch")
            .RequireAuthorization();

        group.MapPost("/dispatches/{id:guid}/dispatch", DispatchStored)
            .WithName("RyvePoolDispatchStoredOrderDelivery")
            .WithSummary("Dispatch a scheduled or failed stored RyvePool delivery")
            .RequireAuthorization();

        group.MapGet("/dispatches/by-external-order/{externalOrderId}", GetByExternalOrderId)
            .WithName("RyvePoolGetOrderDeliveryByExternalOrder")
            .WithSummary("Get a stored RyvePool dispatch by external order ID")
            .RequireAuthorization();

        group.MapGet("/dispatches/{id:guid}/live", GetLive)
            .WithName("RyvePoolGetOrderDeliveryLiveStatus")
            .WithSummary("Get live RyvePool status, courier GPS, and ETA")
            .RequireAuthorization();

        group.MapPatch("/dispatches/{id:guid}/recipient", UpdateRecipient)
            .WithName("RyvePoolUpdateOrderDeliveryRecipient")
            .WithSummary("Update recipient details before pickup")
            .RequireAuthorization();

        group.MapPost("/dispatches/{id:guid}/cancel", CancelDispatch)
            .WithName("RyvePoolCancelOrderDeliveryDispatch")
            .WithSummary("Cancel a RyvePool dispatch before pickup/window lock")
            .RequireAuthorization();

        app.MapPost("/api/public/webhooks/ryvepool", HandleWebhook)
            .WithTags("RyvePool Webhooks")
            .WithName("RyvePoolWebhook")
            .WithSummary("Receive RyvePool dispatch webhooks")
            .AllowAnonymous();
    }

    private static async Task<IResult> GetQuote(
        RyvePoolQuoteRequest req,
        RyvePoolService ryvePool,
        CancellationToken ct)
    {
        try
        {
            var cfg = ryvePool.GetRuntimeConfig();
            var packageType = RyvePoolService.NormalizePackageType(req.PackageType ?? cfg.DefaultPackageType);
            var errors = ValidateQuote(req);
            if (errors.Count > 0)
                return Results.BadRequest(new ApiError("validation_failed", "Some quote details are invalid.", errors));

            var quote = await ryvePool.GetQuoteAsync(new RyvePoolQuoteApiRequest
            {
                PickupLat = req.PickupLat,
                PickupLng = req.PickupLng,
                DropoffLat = req.DropoffLat,
                DropoffLng = req.DropoffLng,
                PickupCity = Clean(req.PickupCity),
                DropoffCity = Clean(req.DropoffCity),
                PackageType = packageType,
                WeightKg = req.WeightKg,
                RegionCode = Clean(req.RegionCode) ?? cfg.DefaultRegionCode,
                VehicleCategoryId = Clean(req.VehicleCategoryId)
            }, ct);

            return Results.Json(quote.Json);
        }
        catch (RyvePoolException ex)
        {
            return RyvePoolError(ex);
        }
    }

    private static async Task<IResult> CreateDispatch(
        RyvePoolDeliveryCreateRequest req,
        HttpContext ctx,
        AppDbContext db,
        RyvePoolService ryvePool,
        CancellationToken ct)
    {
        try
        {
            var userId = GetUserId(ctx);
            var cfg = ryvePool.GetRuntimeConfig();
            var errors = ValidateCreate(req);
            if (errors.Count > 0)
                return Results.BadRequest(new ApiError("validation_failed", "Some dispatch details are invalid.", errors));

            var packageType = RyvePoolService.NormalizePackageType(req.PackageType ?? cfg.DefaultPackageType);
            var paymentType = RyvePoolService.NormalizePaymentType(req.PaymentType);
            var dispatchMode = RyvePoolService.NormalizeDispatchMode(req.DispatchMode ?? cfg.DefaultDispatchMode);
            var externalOrderId = req.ExternalOrderId.Trim();

            var existing = await db.RyvePoolDeliveries
                .AsNoTracking()
                .FirstOrDefaultAsync(d =>
                    d.Environment == cfg.Environment &&
                    d.ExternalOrderId == externalOrderId,
                    ct);
            if (existing is not null)
            {
                if (existing.UserId != userId && !ctx.User.IsInRole("Admin"))
                    return Results.Conflict(new ApiError("duplicate_external_order_id", "This external order ID already has a RyvePool dispatch."));

                return Results.Ok(MapDelivery(existing));
            }

            var apiRequest = new RyvePoolDispatchApiRequest
            {
                ExternalOrderId = externalOrderId,
                MerchantReference = Clean(req.MerchantReference),
                ExternalBranchId = Clean(req.ExternalBranchId) ?? cfg.DefaultExternalBranchId,
                DispatchMode = dispatchMode,
                Pickup = MapAddress(req.Pickup),
                Dropoff = MapAddress(req.Dropoff),
                PaymentType = paymentType,
                CodAmountMinor = req.CodAmountMinor.GetValueOrDefault(),
                PackageType = packageType,
                ParcelWeightKg = req.ParcelWeightKg,
                DriverInstructions = Clean(req.DriverInstructions),
                Metadata = req.Metadata
            };

            var (dispatch, raw) = await ryvePool.CreateDispatchAsync(apiRequest, ct);
            var delivery = new RyvePoolDelivery
            {
                UserId = userId,
                Environment = cfg.Environment,
                ExternalOrderId = externalOrderId,
                MerchantReference = Clean(req.MerchantReference),
                ExternalBranchId = apiRequest.ExternalBranchId,
                DispatchModeRequested = dispatchMode,
                PaymentType = paymentType,
                CodAmountMinor = apiRequest.CodAmountMinor,
                PackageType = packageType,
                ParcelWeightKg = req.ParcelWeightKg,
                DriverInstructions = Clean(req.DriverInstructions),
                RegionCode = cfg.DefaultRegionCode,
                PickupName = req.Pickup.Name.Trim(),
                PickupPhone = req.Pickup.Phone.Trim(),
                PickupAddress = Clean(req.Pickup.Address),
                PickupLandmark = Clean(req.Pickup.Landmark),
                PickupLat = req.Pickup.Lat,
                PickupLng = req.Pickup.Lng,
                DropoffName = req.Dropoff.Name.Trim(),
                DropoffPhone = req.Dropoff.Phone.Trim(),
                DropoffEmail = Clean(req.Dropoff.Email),
                DropoffAddress = Clean(req.Dropoff.Address),
                DropoffLandmark = Clean(req.Dropoff.Landmark),
                DropoffLat = req.Dropoff.Lat,
                DropoffLng = req.Dropoff.Lng,
                MetadataJson = req.Metadata is null ? null : JsonSerializer.Serialize(req.Metadata, JsonOpts),
                RawCreateResponse = raw,
                RawLatestResponse = raw
            };
            ApplyDispatchResponse(delivery, dispatch);

            db.RyvePoolDeliveries.Add(delivery);
            await db.SaveChangesAsync(ct);

            return Results.Created($"/api/order-deliveries/dispatches/{delivery.Id}", MapDelivery(delivery));
        }
        catch (RyvePoolException ex)
        {
            return RyvePoolError(ex);
        }
    }

    private static async Task<IResult> GetDispatch(
        Guid id,
        HttpContext ctx,
        AppDbContext db)
    {
        var delivery = await LoadOwnedDelivery(id, ctx, db);
        return delivery is null
            ? Results.NotFound(new ApiError("not_found", "Order delivery dispatch not found."))
            : Results.Ok(MapDelivery(delivery));
    }

    private static async Task<IResult> DispatchStored(
        Guid id,
        HttpContext ctx,
        AppDbContext db,
        RyvePoolDispatchCoordinator coordinator,
        CancellationToken ct)
    {
        var delivery = await LoadOwnedDelivery(id, ctx, db);
        if (delivery is null)
            return Results.NotFound(new ApiError("not_found", "Order delivery dispatch not found."));

        if (!string.IsNullOrWhiteSpace(delivery.RyvePoolDispatchId))
            return Results.Ok(MapDelivery(delivery));

        var ok = await coordinator.TryDispatchStoredDeliveryAsync(delivery, ct);
        return ok
            ? Results.Ok(MapDelivery(delivery))
            : Results.Json(
                new ApiError("ryvepool_dispatch_failed", delivery.LastDispatchError ?? "RyvePool dispatch failed."),
                statusCode: StatusCodes.Status502BadGateway);
    }

    private static async Task<IResult> GetByExternalOrderId(
        string externalOrderId,
        HttpContext ctx,
        AppDbContext db,
        RyvePoolService ryvePool,
        CancellationToken ct)
    {
        var userId = GetUserId(ctx);
        var environment = ryvePool.GetRuntimeConfig().Environment;
        var delivery = await db.RyvePoolDeliveries
            .AsNoTracking()
            .FirstOrDefaultAsync(d =>
                d.UserId == userId &&
                d.Environment == environment &&
                d.ExternalOrderId == externalOrderId,
                ct);

        return delivery is null
            ? Results.NotFound(new ApiError("not_found", "Order delivery dispatch not found."))
            : Results.Ok(MapDelivery(delivery));
    }

    private static async Task<IResult> GetLive(
        Guid id,
        HttpContext ctx,
        AppDbContext db,
        RyvePoolService ryvePool,
        CancellationToken ct)
    {
        try
        {
            var delivery = await LoadOwnedDelivery(id, ctx, db);
            if (delivery is null)
                return Results.NotFound(new ApiError("not_found", "Order delivery dispatch not found."));
            if (string.IsNullOrWhiteSpace(delivery.RyvePoolDispatchId))
                return Results.Conflict(new ApiError("missing_dispatch_id", "RyvePool dispatch ID is not available yet."));

            var live = await ryvePool.GetLiveAsync(delivery.RyvePoolDispatchId, ct);
            delivery.RawLatestResponse = live.RawJson;
            delivery.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            return Results.Json(live.Json);
        }
        catch (RyvePoolException ex)
        {
            return RyvePoolError(ex);
        }
    }

    private static async Task<IResult> UpdateRecipient(
        Guid id,
        RyvePoolRecipientUpdateRequest req,
        HttpContext ctx,
        AppDbContext db,
        RyvePoolService ryvePool,
        CancellationToken ct)
    {
        try
        {
            var delivery = await LoadOwnedDelivery(id, ctx, db);
            if (delivery is null)
                return Results.NotFound(new ApiError("not_found", "Order delivery dispatch not found."));
            if (string.IsNullOrWhiteSpace(delivery.RyvePoolDispatchId))
                return Results.Conflict(new ApiError("missing_dispatch_id", "RyvePool dispatch ID is not available yet."));
            if (string.IsNullOrWhiteSpace(req.RecipientName) &&
                string.IsNullOrWhiteSpace(req.RecipientPhone) &&
                string.IsNullOrWhiteSpace(req.RecipientEmail))
            {
                return Results.BadRequest(new ApiError("validation_failed", "At least one recipient field is required."));
            }

            var (dispatch, raw) = await ryvePool.UpdateRecipientAsync(
                delivery.RyvePoolDispatchId,
                new RyvePoolRecipientUpdateApiRequest
                {
                    RecipientName = Clean(req.RecipientName),
                    RecipientPhone = Clean(req.RecipientPhone),
                    RecipientEmail = Clean(req.RecipientEmail)
                },
                ct);

            if (!string.IsNullOrWhiteSpace(req.RecipientName))
                delivery.DropoffName = req.RecipientName.Trim();
            if (!string.IsNullOrWhiteSpace(req.RecipientPhone))
                delivery.DropoffPhone = req.RecipientPhone.Trim();
            if (!string.IsNullOrWhiteSpace(req.RecipientEmail))
                delivery.DropoffEmail = req.RecipientEmail.Trim();
            delivery.RawLatestResponse = raw;
            ApplyDispatchResponse(delivery, dispatch);
            await db.SaveChangesAsync(ct);

            return Results.Ok(MapDelivery(delivery));
        }
        catch (RyvePoolException ex)
        {
            return RyvePoolError(ex);
        }
    }

    private static async Task<IResult> CancelDispatch(
        Guid id,
        RyvePoolCancelRequest req,
        HttpContext ctx,
        AppDbContext db,
        RyvePoolService ryvePool,
        CancellationToken ct)
    {
        try
        {
            var delivery = await LoadOwnedDelivery(id, ctx, db);
            if (delivery is null)
                return Results.NotFound(new ApiError("not_found", "Order delivery dispatch not found."));
            if (string.IsNullOrWhiteSpace(delivery.RyvePoolDispatchId))
                return Results.Conflict(new ApiError("missing_dispatch_id", "RyvePool dispatch ID is not available yet."));

            var (dispatch, raw) = await ryvePool.CancelAsync(
                delivery.RyvePoolDispatchId,
                new RyvePoolCancelApiRequest { Reason = Clean(req.Reason) },
                ct);

            delivery.RawLatestResponse = raw;
            ApplyDispatchResponse(delivery, dispatch);
            await db.SaveChangesAsync(ct);

            return Results.Ok(MapDelivery(delivery));
        }
        catch (RyvePoolException ex)
        {
            return RyvePoolError(ex);
        }
    }

    private static async Task<IResult> HandleWebhook(
        HttpRequest request,
        AppDbContext db,
        RyvePoolService ryvePool,
        CancellationToken ct)
    {
        using var reader = new StreamReader(request.Body, Encoding.UTF8);
        var rawBody = await reader.ReadToEndAsync(ct);
        var signature = request.Headers["X-RyvePool-Signature"].FirstOrDefault();
        var deliveryHeader = request.Headers["X-RyvePool-Delivery-Id"].FirstOrDefault();
        var signatureValid = VerifySignature(rawBody, signature, ryvePool.GetConfiguredWebhookSecrets());
        var signatureRequired = ryvePool.GetRuntimeConfig().WebhookSignatureRequired;

        if (signatureRequired && !signatureValid)
            return Results.Unauthorized();

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(rawBody);
        }
        catch (JsonException)
        {
            return Results.BadRequest(new ApiError("invalid_payload", "Webhook body is not valid JSON."));
        }

        using (doc)
        {
            var root = doc.RootElement;
            var data = root.TryGetProperty("data", out var dataElement) ? dataElement : default;
            var eventId = ReadString(root, "id") ?? deliveryHeader;
            var eventName = ReadString(root, "event") ?? request.Headers["X-RyvePool-Event"].FirstOrDefault() ?? "";
            var environment = RyvePoolService.NormalizeEnvironment(ReadString(root, "environment"));
            var dispatchId = data.ValueKind == JsonValueKind.Object ? ReadString(data, "dispatchId") : null;
            var externalOrderId = data.ValueKind == JsonValueKind.Object ? ReadString(data, "externalOrderId") : null;
            var previousStatus = data.ValueKind == JsonValueKind.Object ? ReadString(data, "previousStatus") : null;
            var status = data.ValueKind == JsonValueKind.Object ? ReadString(data, "status") : null;

            if (!string.IsNullOrWhiteSpace(eventId) &&
                await db.RyvePoolWebhookEvents.AnyAsync(e => e.RyvePoolEventId == eventId, ct))
            {
                return Results.Ok(new { received = true, duplicate = true });
            }

            var delivery = await FindDeliveryForWebhook(db, environment, dispatchId, externalOrderId, ct);
            var webhookEvent = new RyvePoolWebhookEvent
            {
                DeliveryId = delivery?.Id,
                RyvePoolEventId = eventId,
                Event = eventName,
                Environment = environment,
                DispatchId = dispatchId,
                ExternalOrderId = externalOrderId,
                PreviousStatus = previousStatus,
                Status = status,
                SignatureHeader = signature,
                DeliveryHeader = deliveryHeader,
                IsSignatureValid = signatureValid,
                RawPayload = rawBody
            };
            db.RyvePoolWebhookEvents.Add(webhookEvent);

            if (delivery is not null)
            {
                ApplyWebhookStatus(delivery, status, data);
                delivery.RawLatestResponse = rawBody;
                delivery.UpdatedAt = DateTime.UtcNow;
            }

            await db.SaveChangesAsync(ct);
        }

        return Results.Ok(new { received = true });
    }

    private static List<FieldError> ValidateQuote(RyvePoolQuoteRequest req)
    {
        var errors = new List<FieldError>();
        var hasPickupCoordinates = req.PickupLat.HasValue && req.PickupLng.HasValue;
        var hasDropoffCoordinates = req.DropoffLat.HasValue && req.DropoffLng.HasValue;
        var hasPickupCity = !string.IsNullOrWhiteSpace(req.PickupCity);
        var hasDropoffCity = !string.IsNullOrWhiteSpace(req.DropoffCity);

        if (!hasPickupCoordinates && !hasPickupCity)
            errors.Add(new("pickup", "Pickup coordinates or pickup city are required."));
        if (!hasDropoffCoordinates && !hasDropoffCity)
            errors.Add(new("dropoff", "Dropoff coordinates or dropoff city are required."));
        if (req.WeightKg.HasValue && req.WeightKg <= 0)
            errors.Add(new("weightKg", "Weight must be greater than 0."));

        return errors;
    }

    private static List<FieldError> ValidateCreate(RyvePoolDeliveryCreateRequest req)
    {
        var errors = new List<FieldError>();
        if (string.IsNullOrWhiteSpace(req.ExternalOrderId))
            errors.Add(new("externalOrderId", "External order ID is required."));

        ValidateAddress(req.Pickup, "pickup", errors);
        ValidateAddress(req.Dropoff, "dropoff", errors);

        if (RyvePoolService.NormalizePaymentType(req.PaymentType) == "cod" && req.CodAmountMinor.GetValueOrDefault() <= 0)
            errors.Add(new("codAmountMinor", "COD amount is required when payment type is cod."));
        if (req.ParcelWeightKg.HasValue && req.ParcelWeightKg <= 0)
            errors.Add(new("parcelWeightKg", "Parcel weight must be greater than 0."));

        return errors;
    }

    private static void ValidateAddress(RyvePoolAddressInput address, string prefix, List<FieldError> errors)
    {
        if (address is null)
        {
            errors.Add(new(prefix, $"{prefix} is required."));
            return;
        }

        if (string.IsNullOrWhiteSpace(address.Name))
            errors.Add(new($"{prefix}.name", "Name is required."));
        if (string.IsNullOrWhiteSpace(address.Phone))
            errors.Add(new($"{prefix}.phone", "Phone is required."));
        if (!address.Lat.HasValue || !address.Lng.HasValue)
        {
            if (string.IsNullOrWhiteSpace(address.Address))
                errors.Add(new($"{prefix}.address", "Address is required when coordinates are not provided."));
        }
    }

    private static RyvePoolDispatchAddressApiRequest MapAddress(RyvePoolAddressInput address) => new()
    {
        Name = address.Name.Trim(),
        Phone = address.Phone.Trim(),
        Address = Clean(address.Address),
        Landmark = Clean(address.Landmark),
        Lat = address.Lat,
        Lng = address.Lng,
        Email = Clean(address.Email)
    };

    private static async Task<RyvePoolDelivery?> LoadOwnedDelivery(Guid id, HttpContext ctx, AppDbContext db)
    {
        var userId = GetUserId(ctx);
        var isAdmin = ctx.User.IsInRole("Admin");
        return await db.RyvePoolDeliveries
            .FirstOrDefaultAsync(d => d.Id == id && (isAdmin || d.UserId == userId));
    }

    private static async Task<RyvePoolDelivery?> FindDeliveryForWebhook(
        AppDbContext db,
        string environment,
        string? dispatchId,
        string? externalOrderId,
        CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(dispatchId))
        {
            var byDispatchId = await db.RyvePoolDeliveries
                .FirstOrDefaultAsync(d => d.Environment == environment && d.RyvePoolDispatchId == dispatchId, ct);
            if (byDispatchId is not null)
                return byDispatchId;
        }

        if (!string.IsNullOrWhiteSpace(externalOrderId))
        {
            return await db.RyvePoolDeliveries
                .FirstOrDefaultAsync(d => d.Environment == environment && d.ExternalOrderId == externalOrderId, ct);
        }

        return null;
    }

    private static void ApplyDispatchResponse(RyvePoolDelivery delivery, RyvePoolDispatchApiResponse dispatch)
    {
        if (!string.IsNullOrWhiteSpace(dispatch.Id))
            delivery.RyvePoolDispatchId = dispatch.Id;
        if (!string.IsNullOrWhiteSpace(dispatch.ExternalOrderId))
            delivery.ExternalOrderId = dispatch.ExternalOrderId;
        delivery.MerchantReference = dispatch.MerchantReference ?? delivery.MerchantReference;
        delivery.Status = string.IsNullOrWhiteSpace(dispatch.Status) ? delivery.Status : dispatch.Status;
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

    private static void ApplyWebhookStatus(RyvePoolDelivery delivery, string? status, JsonElement data)
    {
        if (!string.IsNullOrWhiteSpace(status))
            delivery.Status = status;

        delivery.PickedUpAt = ReadDateTime(data, "pickedUpAt") ?? delivery.PickedUpAt;
        delivery.DeliveredAt = ReadDateTime(data, "deliveredAt") ?? delivery.DeliveredAt;
        delivery.CancelledAt = ReadDateTime(data, "cancelledAt") ?? delivery.CancelledAt;
        delivery.FailedAt = ReadDateTime(data, "failedAt") ?? delivery.FailedAt;

        if (delivery.Status.Equals("cancelled", StringComparison.OrdinalIgnoreCase))
            delivery.CanCancel = false;
        if (delivery.Status is "picked_up" or "en_route" or "delivered")
            delivery.CanCancel = false;
    }

    private static RyvePoolDeliveryResponse MapDelivery(RyvePoolDelivery d) => new(
        d.Id,
        d.QuoteId,
        d.ShipmentId,
        d.Environment,
        d.ExternalOrderId,
        d.RyvePoolDispatchId,
        d.Status,
        d.TrackingUrl,
        d.RegionCode,
        d.ExternalBranchId,
        d.DispatchModeUsed,
        d.PaymentType,
        d.CodAmountMinor,
        d.PackageType,
        d.Currency,
        d.DeliveryFeeMinor,
        d.PlatformFeeMinor,
        d.RyvePoolCommissionMinor,
        d.DriverPayoutMinor,
        d.CanCancel,
        d.CancellableUntil,
        d.DispatchTiming,
        d.ScheduledForUtc,
        d.DispatchAttemptCount,
        d.LastDispatchAttemptAt,
        d.LastDispatchError,
        d.DhlPointId,
        d.DhlPointName,
        d.CreatedAt,
        d.UpdatedAt);

    private static bool VerifySignature(string rawBody, string? signatureHeader, IEnumerable<string> secrets)
    {
        if (string.IsNullOrWhiteSpace(signatureHeader))
            return false;

        foreach (var secret in secrets)
        {
            var expected = "sha256=" + Convert.ToHexString(
                HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(rawBody)))
                .ToLowerInvariant();

            var expectedBytes = Encoding.UTF8.GetBytes(expected);
            var actualBytes = Encoding.UTF8.GetBytes(signatureHeader.Trim());
            if (expectedBytes.Length == actualBytes.Length &&
                CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes))
            {
                return true;
            }
        }

        return false;
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.String)
            return null;

        return property.GetString();
    }

    private static DateTime? ReadDateTime(JsonElement element, string propertyName)
    {
        var value = ReadString(element, propertyName);
        return DateTime.TryParse(value, out var parsed) ? parsed.ToUniversalTime() : null;
    }

    private static IResult RyvePoolError(RyvePoolException ex)
    {
        var statusCode = ex.HttpStatusCode == 0 ? StatusCodes.Status503ServiceUnavailable : ex.HttpStatusCode;
        return Results.Json(new ApiError(ex.ErrorCode, ex.Message), statusCode: statusCode);
    }

    private static Guid GetUserId(HttpContext ctx)
    {
        var sub = ctx.User.FindFirstValue(ClaimTypes.NameIdentifier)
                  ?? ctx.User.FindFirstValue("sub")
                  ?? throw new UnauthorizedAccessException();
        return Guid.Parse(sub);
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
