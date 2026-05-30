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
        ILogger<Program> logger)
    {
        var userId = GetUserId(ctx);

        // ── 1. Idempotency: return existing shipment for this quote ──────────
        var existingShipment = await db.Shipments
            .Include(s => s.Packages)
            .FirstOrDefaultAsync(s => s.QuoteId == req.QuoteId && s.UserId == userId);

        if (existingShipment is not null)
        {
            return Results.Ok(BuildConfirmResponse(existingShipment, db));
        }

        // ── 2. Load and validate quote ────────────────────────────────────────
        var quote = await db.Quotes.FirstOrDefaultAsync(q => q.Id == req.QuoteId);
        if (quote is null)
            return Results.NotFound(new ApiError("not_found", "Quote not found."));

        if (quote.ExpiresAt < DateTime.UtcNow)
            return Results.Conflict(new ApiError("quote_expired", "Quote has expired. Please request a new one."));

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
        var isDocuments = quote.ProductCode.Equals("D", StringComparison.OrdinalIgnoreCase);
        var customsItems = req.CustomsItems?.Count > 0
            ? req.CustomsItems
            : BuildDefaultCustomsItems(quote);

        // Parcels require a real HS code on every line item
        if (!isDocuments)
        {
            var missingHs = customsItems
                .Select((ci, i) => (ci, i))
                .Where(x => string.IsNullOrWhiteSpace(x.ci.HsCode))
                .Select(x => new FieldError($"customsItems[{x.i}].hsCode",
                    "HS code is required for parcel customs items. Find yours at hts.usitc.gov or trade-tariff.service.gov.uk."))
                .ToList();
            if (missingHs.Count > 0)
                return Results.BadRequest(new ApiError("validation_failed",
                    "One or more customs items are missing an HS code.", missingHs));
        }

        var exportReason = req.ExportReason ?? quote.CustomsReason ?? "SOLD";

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
            DhlBaseRate         = quote.DhlBaseRate,
            MarkupPercent       = quote.MarkupPercent,
            PlatformFee         = quote.PlatformFee,
            TotalAmount         = quote.TotalAmount,
            Currency            = quote.Currency,
            Status              = "PaymentAuthorized",
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
                Description         = ci.Description,
                Quantity            = ci.Quantity,
                UnitOfMeasurement   = ci.UnitOfMeasurement ?? "PCS",
                UnitPrice           = ci.UnitPrice,
                Currency            = ci.Currency ?? quote.Currency,
                HsCode              = ci.HsCode!,
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

            await ShipmentEndpoints.BookDhlShipmentAsync(fullShipment, db, dhl, logger);
            await db.SaveChangesAsync();

            // Return success
            var reloaded = await db.Shipments.Include(s => s.Packages).FirstAsync(s => s.Id == shipment.Id);
            return Results.Ok(BuildConfirmResponse(reloaded, db));
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

            return Results.UnprocessableEntity(new BookingConfirmResponse(
                shipment.Id, null, "failed", new(), refundId));
        }
        catch (DhlException ex)
        {
            // Transient DHL 5xx — keep payment alive, allow retry
            logger.LogError(ex, "Transient DHL error for shipment {Id}", shipment.Id);
            shipment.Status = "Booked"; // payment OK, DHL pending retry
            await db.SaveChangesAsync();
            return Results.StatusCode(502);
        }
    }

    // ─── Helpers ───────────────────────────────────────────────────────────

    private static BookingConfirmResponse BuildConfirmResponse(Shipment s, AppDbContext db)
    {
        var baseUrl = $"/api/shipments/{s.Id}/documents";
        var docs = new List<DocumentInfo>
        {
            new("label",   $"{baseUrl}/label",   s.LabelFilePath   is not null && File.Exists(s.LabelFilePath)),
            new("invoice", $"{baseUrl}/invoice", s.InvoiceFilePath is not null && File.Exists(s.InvoiceFilePath)),
            new("waybill", $"{baseUrl}/waybill", s.WaybillFilePath is not null && File.Exists(s.WaybillFilePath)),
        };

        return new BookingConfirmResponse(s.Id, s.TrackingNumber, MapStatus(s.Status), docs, null);
    }

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

    private static Guid GetUserId(HttpContext ctx)
    {
        var sub = ctx.User.FindFirstValue(ClaimTypes.NameIdentifier)
               ?? ctx.User.FindFirstValue("sub")
               ?? throw new UnauthorizedAccessException();
        return Guid.Parse(sub);
    }
}
