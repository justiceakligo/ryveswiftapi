using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using RyveSwift.Api.Common;
using RyveSwift.Api.Data;
using RyveSwift.Api.Dtos;
using RyveSwift.Api.Entities;
using RyveSwift.Api.Services;

namespace RyveSwift.Api.Endpoints;

public static class BookingEndpoints
{
    public static void MapBookingEndpoints(this WebApplication app)
    {
        app.MapPost("/api/bookings/confirm", ConfirmBooking)
            .WithTags("Bookings")
            .WithName("ConfirmBooking")
            .WithSummary("Confirm a booking after Stripe payment — creates shipment and DHL label")
            .RequireAuthorization();
    }

    // ─── POST /api/bookings/confirm ─────────────────────────────────────────

    private static async Task<IResult> ConfirmBooking(
        ConfirmBookingRequest req,
        HttpContext ctx,
        AppDbContext db,
        StripeService stripe,
        DhlService dhl,
        RyvePoolDispatchCoordinator ryvePoolDispatch,
        SpacesStorageService spaces,
        ILogger<Program> logger,
        NotificationEmailService emails)
    {
        var userId = GetUserId(ctx);

        // ── 1. Idempotency: return existing shipment for this quote ──────────
        var existingShipment = await db.Shipments
            .Include(s => s.Packages)
            .FirstOrDefaultAsync(s => s.QuoteId == req.QuoteId && s.UserId == userId);

        if (existingShipment is not null)
        {
            return Results.Ok(await BuildConfirmResponseAsync(existingShipment, db));
        }

        // ── 2. Load and validate quote ────────────────────────────────────────
        var quote = await db.Quotes.FirstOrDefaultAsync(q => q.Id == req.QuoteId);
        if (quote is null || (quote.UserId.HasValue && quote.UserId.Value != userId))
            return Results.NotFound(new ApiError("not_found", "Quote not found."));

        if (quote.ExpiresAt < DateTime.UtcNow)
            return Results.Conflict(new ApiError("quote_expired", "Quote has expired. Please request a new one."));

        if (!quote.UserId.HasValue)
            quote.UserId = userId;

        // ── 3. Verify PaymentIntent status ────────────────────────────────────
        Stripe.PaymentIntent pi;
        try
        {
            pi = await stripe.GetPaymentIntentAsync(req.PaymentIntentId);
        }
        catch (Stripe.StripeException)
        {
            return Results.UnprocessableEntity(new ApiError("payment_not_found", "PaymentIntent not found."));
        }

        if (pi.Status != "succeeded")
            return Results.UnprocessableEntity(new ApiError("payment_not_complete",
                $"PaymentIntent status is '{pi.Status}', expected 'succeeded'."));

        // ── 4. Verify PI amount matches quote (within 1 cent) ─────────────────
        var expectedAmountCents = (long)Math.Round(quote.TotalAmount * 100, MidpointRounding.AwayFromZero);
        if (Math.Abs(pi.Amount - expectedAmountCents) > 1)
            return Results.UnprocessableEntity(new ApiError("amount_mismatch",
                "PaymentIntent amount does not match quote amount."));

        // ── 5. Resolve sender address ─────────────────────────────────────────
        Address? senderAddress = null;
        if (req.SenderAddressId.HasValue)
        {
            senderAddress = await db.Addresses.FirstOrDefaultAsync(
                a => a.Id == req.SenderAddressId.Value && a.UserId == userId);
            if (senderAddress is null)
                return Results.NotFound(new ApiError("not_found", "Sender address not found."));
        }
        else
        {
            senderAddress = await db.Addresses.FirstOrDefaultAsync(
                a => a.UserId == userId && a.IsDefaultSender);
            if (senderAddress is null)
                return Results.NotFound(new ApiError("not_found",
                    "No default sender address found. Please provide senderAddressId."));
        }

        // ── 6. Resolve receiver address ───────────────────────────────────────
        var receiverAddress = await db.Addresses.FirstOrDefaultAsync(
            a => a.Id == req.ReceiverAddressId && a.UserId == userId);
        if (receiverAddress is null)
            return Results.NotFound(new ApiError("not_found", "Receiver address not found."));

        // ── 7. Compute customs values (use quote customs if not overridden) ───
        var needsCustoms = RequiresCustoms(quote);
        var customsItems = needsCustoms
            ? req.CustomsItems?.Count > 0
                ? req.CustomsItems
                : BuildDefaultCustomsItems(quote)
            : new List<CustomsItemRequest>();

        // Parcels require a real HS code on every line item
        if (needsCustoms)
        {
            var customsErrors = CustomsValidation.ValidateCustomsItemRequests(customsItems, requireHsCode: true);
            if (customsErrors.Count > 0)
                return Results.BadRequest(new ApiError("validation_failed",
                    "One or more customs items are missing required clearance details.", customsErrors));
        }

        var exportReason = req.ExportReason ?? quote.CustomsReason ?? "sale";
        var incoterm = NormalizeIncoterm(req.Incoterm ?? quote.Incoterm);
        if (incoterm == "DDP" && quote.OriginCountry.Equals(quote.DestinationCountry, StringComparison.OrdinalIgnoreCase))
            return Results.BadRequest(new ApiError("validation_failed", "DDP can only be used for international shipments."));

        // ── 8. Create Shipment entity ─────────────────────────────────────────
        var shipment = new Shipment
        {
            UserId              = userId,
            QuoteId             = quote.Id,
            SenderAddressId     = senderAddress.Id,
            ReceiverAddressId   = receiverAddress.Id,
            OriginCountry       = quote.OriginCountry,
            DestinationCountry  = quote.DestinationCountry,
            ProductCode         = quote.ProductCode,
            Incoterm            = incoterm,
            DhlBaseRate         = quote.DhlBaseRate,
            MarkupPercent       = quote.MarkupPercent,
            PlatformFee         = quote.PlatformFee,
            TotalAmount         = quote.TotalAmount,
            Currency            = quote.Currency,
            Status              = "PaymentAuthorized",
            ExportReason        = exportReason.Trim(),
            InvoiceNumber       = req.InvoiceNumber?.Trim(),
            InvoiceDate         = req.InvoiceDate
        };
        db.Shipments.Add(shipment);

        // ── 9. Create package ─────────────────────────────────────────────────
        db.ShipmentPackages.Add(new ShipmentPackage
        {
            ShipmentId  = shipment.Id,
            WeightKg    = quote.WeightKg,
            LengthCm    = quote.LengthCm,
            WidthCm     = quote.WidthCm,
            HeightCm    = quote.HeightCm,
        });

        // ── 10. Persist customs items ─────────────────────────────────────────
        foreach (var ci in customsItems)
        {
            db.CustomsItems.Add(new CustomsItem
            {
                ShipmentId          = shipment.Id,
                Description         = CustomsValidation.NormalizeDescription(ci.Description),
                Quantity            = ci.Quantity,
                UnitOfMeasurement   = ci.UnitOfMeasurement ?? "PCS",
                UnitPrice           = ci.UnitPrice,
                Currency            = ci.Currency ?? quote.Currency,
                HsCode              = CustomsValidation.NormalizeHsCode(ci.HsCode),
                ManufacturerCountry = ci.ManufacturerCountry ?? senderAddress.CountryCode,
                NetWeightKg         = ci.NetWeightKg ?? (quote.WeightKg / Math.Max(ci.Quantity, 1)),
                GrossWeightKg       = ci.GrossWeightKg ?? (quote.WeightKg / Math.Max(ci.Quantity, 1)),
            });
        }

        // ── 11. Link payment record to shipment ────────────────────────────────
        var payment = await db.Payments.FirstOrDefaultAsync(
            p => p.StripePaymentIntentId == req.PaymentIntentId);
        if (payment is not null)
        {
            payment.ShipmentId = shipment.Id;
            payment.Status     = "succeeded";
        }

        await db.SaveChangesAsync();

        // ── 12. Book DHL shipment + labels ─────────────────────────────────────
        try
        {
            // Reload with full navigation properties for DHL booking
            var fullShipment = await db.Shipments
                .Include(s => s.SenderAddress)
                .Include(s => s.ReceiverAddress)
                .Include(s => s.Packages)
                .Include(s => s.CustomsItems)
                .FirstAsync(s => s.Id == shipment.Id);

            await ShipmentEndpoints.BookDhlShipmentAsync(fullShipment, db, dhl, spaces, logger);
            await db.SaveChangesAsync();

            RyvePoolDelivery? orderDelivery = null;
            try
            {
                orderDelivery = await ryvePoolDispatch.CreateShipmentDeliveryAsync(quote, fullShipment, userId, ctx.RequestAborted);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "RyvePool delivery add-on failed after DHL booking for shipment {Id}", shipment.Id);
                db.ShipmentEvents.Add(new ShipmentEvent
                {
                    ShipmentId = shipment.Id,
                    EventType = "RyvePoolDeliveryFailed",
                    Description = ex.Message
                });
                await db.SaveChangesAsync();
            }

            // Return success
            var reloaded = await db.Shipments.Include(s => s.Packages).FirstAsync(s => s.Id == shipment.Id);
            var user = await db.Users.FindAsync(userId);
            await emails.SendShipmentLabelCreatedAsync(reloaded, user);
            return Results.Ok(await BuildConfirmResponseAsync(reloaded, db, orderDelivery));
        }
        catch (DhlException ex) when (ex.IsClientError)
        {
            // Hard DHL 4xx error — auto-refund
            string? refundId = null;
            try
            {
                var refund = await stripe.RefundPaymentIntentAsync(req.PaymentIntentId);
                refundId = refund.Id;
                if (payment is not null) payment.Status = "refunded";
                shipment.Status = "Refunded";
                await db.SaveChangesAsync();
            }
            catch (Exception refundEx)
            {
                logger.LogError(refundEx, "Auto-refund failed after DHL hard error for shipment {Id}", shipment.Id);
            }

            var user = await db.Users.FindAsync(userId);
            await emails.SendBookingFailureAsync(shipment, user, ex.Message, refundId);

            return Results.UnprocessableEntity(new BookingConfirmResponse(
                shipment.Id, null, "failed", new(), null, refundId));
        }
        catch (DhlException ex)
        {
            // Transient DHL 5xx — keep payment alive, allow retry
            logger.LogError(ex, "Transient DHL error for shipment {Id}", shipment.Id);
            shipment.Status = "Booked"; // payment OK, DHL pending retry
            await db.SaveChangesAsync();
            var user = await db.Users.FindAsync(userId);
            await emails.SendDhlTransientFailureAdminAlertAsync(shipment, user, ex.Message);
            return Results.StatusCode(502);
        }
    }

    // ─── Helpers ───────────────────────────────────────────────────────────

    private static async Task<BookingConfirmResponse> BuildConfirmResponseAsync(
        Shipment s,
        AppDbContext db,
        RyvePoolDelivery? orderDelivery = null)
    {
        var baseUrl = $"/api/shipments/{s.Id}/documents";
        var docs = new List<DocumentInfo>
        {
            new("label",   $"{baseUrl}/label",   s.LabelFilePath   is not null),
            new("invoice", $"{baseUrl}/invoice", s.InvoiceFilePath is not null),
            new("waybill", $"{baseUrl}/waybill", s.WaybillFilePath is not null),
        };

        orderDelivery ??= await db.RyvePoolDeliveries
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.ShipmentId == s.Id || d.QuoteId == s.QuoteId);

        return new BookingConfirmResponse(
            s.Id,
            s.TrackingNumber,
            MapStatus(s.Status),
            docs,
            orderDelivery is null ? null : MapOrderDelivery(orderDelivery),
            null);
    }

    private static RyvePoolDeliveryResponse MapOrderDelivery(RyvePoolDelivery d) => new(
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

    private static List<CustomsItemRequest> BuildDefaultCustomsItems(Quote quote)
    {
        if (!string.IsNullOrWhiteSpace(quote.CustomsCategory))
        {
            return new List<CustomsItemRequest>
            {
                new(
                    quote.CustomsCategory,
                    1,
                    "PCS",
                    quote.CustomsDeclaredValue ?? quote.TotalAmount,
                    quote.CustomsCurrency ?? quote.Currency,
                    null, null, null, null)  // HsCode intentionally null — validated by caller
            };
        }

        // Minimal fallback so DHL doesn't reject the shipment
        return new List<CustomsItemRequest>
        {
            new("General Goods", 1, "PCS", quote.TotalAmount, quote.Currency,
                null, null, null, null)  // HsCode intentionally null — validated by caller
        };
    }

    private static bool RequiresCustoms(Quote quote) =>
        !quote.OriginCountry.Equals(quote.DestinationCountry, StringComparison.OrdinalIgnoreCase) &&
        !quote.ProductCode.Equals("D", StringComparison.OrdinalIgnoreCase);

    private static string MapStatus(string status) => status switch
    {
        "PendingPayment" or "PaymentFailed" => "pending_payment",
        "PaymentAuthorized" or "Booked"     => "paid",
        "LabelGenerated"    => "label_created",
        "DroppedOff"        => "dropped_off",
        "InTransit"         => "in_transit",
        "OutForDelivery"    => "out_for_delivery",
        "Delivered"         => "delivered",
        "Exception"         => "exception",
        "Cancelled"         => "cancelled",
        "Refunded"          => "refunded",
        _                   => status.ToLower()
    };

    private static string NormalizeIncoterm(string? incoterm) =>
        incoterm?.Trim().ToUpperInvariant() switch
        {
            "DDP" => "DDP",
            _ => "DAP"
        };

    private static Guid GetUserId(HttpContext ctx)
    {
        var sub = ctx.User.FindFirstValue(ClaimTypes.NameIdentifier)
               ?? ctx.User.FindFirstValue("sub")
               ?? throw new UnauthorizedAccessException();
        return Guid.Parse(sub);
    }
}
