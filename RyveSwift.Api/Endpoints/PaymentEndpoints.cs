using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using RyveSwift.Api.Common;
using RyveSwift.Api.Data;
using RyveSwift.Api.Dtos;
using RyveSwift.Api.Entities;
using RyveSwift.Api.Services;

namespace RyveSwift.Api.Endpoints;

public static class PaymentEndpoints
{
    public static void MapPaymentEndpoints(this WebApplication app)
    {
        var authGroup = app.MapGroup("/api/payments").WithTags("Payments").RequireAuthorization();

        authGroup.MapPost("/create-intent", CreatePaymentIntent)
            .WithName("CreatePaymentIntent")
            .WithSummary("Create (or reuse) a Stripe PaymentIntent for a quote");

        authGroup.MapGet("/{paymentIntentId}/status", GetPaymentStatus)
            .WithName("GetPaymentStatus")
            .WithSummary("Poll the latest payment + booking status (used for inline reconciliation)");

        // Stripe webhook � no auth, signature verification instead
        app.MapPost("/api/public/webhooks/stripe", HandleStripeWebhook)
            .WithTags("Webhooks")
            .WithName("StripeWebhook")
            .WithSummary("Stripe webhook handler (idempotent)")
            .AllowAnonymous();
    }

    // --- POST /api/payments/create-intent ----------------------------------

    private static async Task<IResult> CreatePaymentIntent(
        CreatePaymentIntentRequest req, HttpContext ctx,
        AppDbContext db, StripeService stripe)
    {
        var userId = GetUserId(ctx);
        var idempotencyKey = ctx.Request.Headers["Idempotency-Key"].ToString();

        var quote = await db.Quotes.FirstOrDefaultAsync(q => q.Id == req.QuoteId);
        if (quote is null)
            return Results.NotFound(new ApiError("not_found", "Quote not found."));

        if (quote.ExpiresAt < DateTime.UtcNow)
            return Results.Conflict(new ApiError("quote_expired", "This quote has expired. Please request a new one."));

        // Idempotency: if a live PI already exists for this quote, return it
        var existingPayment = await db.Payments.FirstOrDefaultAsync(
            p => p.QuoteId == req.QuoteId && p.Status == "pending");

        if (existingPayment is not null)
        {
            try
            {
                var existingPi = await stripe.GetPaymentIntentAsync(existingPayment.StripePaymentIntentId);
                // Only reuse if the PI is still open
                if (existingPi.Status is "requires_payment_method" or "requires_confirmation" or "requires_action")
                {
                    return Results.Ok(new PaymentIntentResponse(
                        existingPi.ClientSecret!,
                        existingPi.Id,
                        existingPi.Amount,
                        existingPi.Currency,
                        existingPi.Status));
                }
            }
            catch { /* PI may have been cancelled; fall through to create a new one */ }
        }

        try
        {
            var pi = await stripe.CreatePaymentIntentAsync(
                quote.TotalAmount,
                quote.Currency,
                quote.Id,
                string.IsNullOrWhiteSpace(idempotencyKey) ? null : $"pi-{idempotencyKey}");

            var payment = new Payment
            {
                QuoteId               = quote.Id,
                StripePaymentIntentId = pi.Id,
                Amount                = quote.TotalAmount,
                Currency              = quote.Currency,
                Status                = "pending",
                IdempotencyKey        = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey,
            };
            db.Payments.Add(payment);
            await db.SaveChangesAsync();

            return Results.Ok(new PaymentIntentResponse(
                pi.ClientSecret!,
                pi.Id,
                pi.Amount,
                pi.Currency,
                pi.Status));
        }
        catch (Stripe.StripeException)
        {
            // Stripe declined or encountered an error creating the intent
            return Results.StatusCode(402);
        }
    }

    // --- GET /api/payments/{paymentIntentId}/status -------------------------

    private static async Task<IResult> GetPaymentStatus(
        string paymentIntentId, HttpContext ctx,
        AppDbContext db, StripeService stripe)
    {
        var userId = GetUserId(ctx);

        // Look up in Stripe for the live payment status
        Stripe.PaymentIntent pi;
        try
        {
            pi = await stripe.GetPaymentIntentAsync(paymentIntentId);
        }
        catch (Stripe.StripeException)
        {
            return Results.NotFound(new ApiError("not_found", "PaymentIntent not found."));
        }

        // Find associated shipment via payment record
        var payment = await db.Payments.FirstOrDefaultAsync(p => p.StripePaymentIntentId == paymentIntentId);
        Shipment? shipment = null;
        if (payment?.ShipmentId.HasValue == true)
        {
            shipment = await db.Shipments.FindAsync(payment.ShipmentId.Value);
        }
        // Also check by quoteId in case BookingConfirm created the shipment
        if (shipment is null && payment?.QuoteId.HasValue == true)
        {
            shipment = await db.Shipments.FirstOrDefaultAsync(s => s.QuoteId == payment.QuoteId);
        }

        var bookingStatus = DeriveBookingStatus(payment?.Status, shipment);
        var rejectionReason = (bookingStatus == "failed")
            ? await GetRejectionReason(shipment?.Id, db)
            : null;

        return Results.Ok(new PaymentStatusResponse(
            paymentIntentId,
            pi.Status,
            bookingStatus,
            rejectionReason,
            shipment?.Id,
            shipment?.TrackingNumber));
    }

    // --- POST /api/public/webhooks/stripe -----------------------------------

    private static async Task<IResult> HandleStripeWebhook(
        HttpContext ctx, AppDbContext db,
        StripeService stripe, ILogger<Program> logger)
    {
        string json;
        using (var reader = new StreamReader(ctx.Request.Body))
            json = await reader.ReadToEndAsync();

        var sig = ctx.Request.Headers["Stripe-Signature"].ToString();
        if (string.IsNullOrWhiteSpace(sig))
            return Results.BadRequest(new ApiError("stripe_webhook_invalid", "Missing Stripe-Signature header."));

        Stripe.Event stripeEvent;
        try
        {
            stripeEvent = stripe.ConstructWebhookEvent(json, sig);
        }
        catch (Stripe.StripeException ex)
        {
            logger.LogWarning("Invalid Stripe webhook signature: {Message}", ex.Message);
            return Results.BadRequest(new ApiError("stripe_webhook_invalid", "Webhook signature verification failed."));
        }

        // Idempotency: skip if this event was already processed
        var alreadyProcessed = await db.ShipmentEvents
            .AnyAsync(e => e.EventType == "StripeWebhook" && e.Description == stripeEvent.Id);
        if (alreadyProcessed)
            return Results.Ok();

        switch (stripeEvent.Type)
        {
            case "payment_intent.succeeded":
                if (stripeEvent.Data.Object is Stripe.PaymentIntent succeededPi)
                    await HandlePaymentSucceeded(succeededPi, db, logger);
                break;

            case "payment_intent.payment_failed":
                if (stripeEvent.Data.Object is Stripe.PaymentIntent failedPi)
                    await HandlePaymentFailed(failedPi, db, logger);
                break;

            case "charge.refunded":
                if (stripeEvent.Data.Object is Stripe.Charge refundedCharge)
                    await HandleChargeRefunded(refundedCharge, db, logger);
                break;

            case "charge.dispute.created":
                if (stripeEvent.Data.Object is Stripe.Dispute dispute)
                    await HandleDisputeCreated(dispute, db, logger);
                break;
        }

        // Record that we've processed this Stripe event (idempotency anchor)
        db.ShipmentEvents.Add(new ShipmentEvent
        {
            ShipmentId  = Guid.Empty, // no shipment context; still persisted for dedup
            EventType   = "StripeWebhook",
            Description = stripeEvent.Id
        });
        await db.SaveChangesAsync();

        return Results.Ok();
    }

    // --- Webhook helpers ---------------------------------------------------

    private static async Task HandlePaymentSucceeded(
        Stripe.PaymentIntent intent, AppDbContext db, ILogger logger)
    {
        var payment = await db.Payments.FirstOrDefaultAsync(p => p.StripePaymentIntentId == intent.Id);
        if (payment is null || payment.Status == "succeeded") return; // idempotent

        payment.Status    = "succeeded";
        payment.UpdatedAt = DateTime.UtcNow;

        // If a shipment already exists (BookingConfirm was faster), just mark payment
        var shipment = payment.ShipmentId.HasValue
            ? await db.Shipments.FindAsync(payment.ShipmentId.Value)
            : null;

        if (shipment is null && payment.QuoteId.HasValue)
            shipment = await db.Shipments.FirstOrDefaultAsync(s => s.QuoteId == payment.QuoteId);

        if (shipment is not null && shipment.Status == "PendingPayment")
        {
            shipment.Status    = "PaymentAuthorized";
            shipment.UpdatedAt = DateTime.UtcNow;
            db.ShipmentEvents.Add(new ShipmentEvent
            {
                ShipmentId  = shipment.Id,
                EventType   = "PaymentAuthorized",
                Description = $"Stripe webhook: PI {intent.Id} succeeded."
            });
        }

        await db.SaveChangesAsync();
        logger.LogInformation("Webhook: PI {IntentId} succeeded", intent.Id);
    }

    private static async Task HandlePaymentFailed(
        Stripe.PaymentIntent intent, AppDbContext db, ILogger logger)
    {
        var payment = await db.Payments.FirstOrDefaultAsync(p => p.StripePaymentIntentId == intent.Id);
        if (payment is null || payment.Status == "failed") return;

        payment.Status    = "failed";
        payment.UpdatedAt = DateTime.UtcNow;

        var shipment = payment.ShipmentId.HasValue
            ? await db.Shipments.FindAsync(payment.ShipmentId.Value)
            : null;

        if (shipment is not null)
        {
            shipment.Status    = "PaymentFailed";
            shipment.UpdatedAt = DateTime.UtcNow;
            db.ShipmentEvents.Add(new ShipmentEvent
            {
                ShipmentId  = shipment.Id,
                EventType   = "PaymentFailed",
                Description = $"Stripe webhook: PI {intent.Id} failed."
            });
        }

        await db.SaveChangesAsync();
        logger.LogWarning("Webhook: PI {IntentId} payment_failed", intent.Id);
    }

    private static async Task HandleChargeRefunded(
        Stripe.Charge charge, AppDbContext db, ILogger logger)
    {
        if (charge.PaymentIntentId is null) return;
        var payment = await db.Payments.FirstOrDefaultAsync(p => p.StripePaymentIntentId == charge.PaymentIntentId);
        if (payment is null) return;

        payment.Status    = "refunded";
        payment.UpdatedAt = DateTime.UtcNow;

        var shipment = payment.ShipmentId.HasValue
            ? await db.Shipments.FindAsync(payment.ShipmentId.Value)
            : null;

        if (shipment is not null && shipment.Status != "Refunded")
        {
            shipment.Status    = "Refunded";
            shipment.UpdatedAt = DateTime.UtcNow;
            db.ShipmentEvents.Add(new ShipmentEvent
            {
                ShipmentId  = shipment.Id,
                EventType   = "Refunded",
                Description = $"Charge {charge.Id} refunded."
            });
        }

        await db.SaveChangesAsync();
        logger.LogInformation("Webhook: charge {ChargeId} refunded", charge.Id);
    }

    private static async Task HandleDisputeCreated(
        Stripe.Dispute dispute, AppDbContext db, ILogger logger)
    {
        if (dispute.ChargeId is null) return;
        // Flag for manual review by finding the payment via charge
        var charge = await new Stripe.ChargeService().GetAsync(dispute.ChargeId);
        if (charge?.PaymentIntentId is null) return;

        var payment = await db.Payments.FirstOrDefaultAsync(p => p.StripePaymentIntentId == charge.PaymentIntentId);
        if (payment?.ShipmentId is not null)
        {
            db.ShipmentEvents.Add(new ShipmentEvent
            {
                ShipmentId  = payment.ShipmentId.Value,
                EventType   = "DisputeCreated",
                Description = $"Stripe dispute {dispute.Id} opened. Reason: {dispute.Reason}. Amount: {dispute.Amount}."
            });
            await db.SaveChangesAsync();
        }

        logger.LogWarning("Webhook: dispute {DisputeId} created for charge {ChargeId}", dispute.Id, dispute.ChargeId);
    }

    // --- Helpers -----------------------------------------------------------

    private static string DeriveBookingStatus(string? paymentStatus, Shipment? shipment)
    {
        if (shipment is not null)
        {
            return shipment.Status switch
            {
                "LabelGenerated" or "DroppedOff" or "InTransit"
                    or "OutForDelivery" or "Delivered" => MapStatus(shipment.Status),
                "Cancelled"  => "cancelled",
                "Refunded"   => "refunded",
                "Exception"  => "exception",
                "DhlBookingFailed" or "PaymentFailed" => "failed",
                _ => "paid"
            };
        }

        return paymentStatus switch
        {
            "succeeded" => "paid",
            "failed"    => "failed",
            _           => "pending"
        };
    }

    private static async Task<string?> GetRejectionReason(Guid? shipmentId, AppDbContext db)
    {
        if (shipmentId is null) return null;
        var ev = await db.ShipmentEvents
            .Where(e => e.ShipmentId == shipmentId && e.EventType == "DhlBookingFailed")
            .OrderByDescending(e => e.CreatedAt)
            .FirstOrDefaultAsync();
        return ev?.Description;
    }

    private static string MapStatus(string status) => status switch
    {
        "PendingPayment" or "PaymentFailed" => "pending_payment",
        "PaymentAuthorized" or "Booked"    => "paid",
        "LabelGenerated"   => "label_created",
        "DroppedOff"       => "dropped_off",
        "InTransit"        => "in_transit",
        "OutForDelivery"   => "out_for_delivery",
        "Delivered"        => "delivered",
        "Exception"        => "exception",
        "Cancelled"        => "cancelled",
        "Refunded"         => "refunded",
        _                  => status.ToLower()
    };

    private static Guid GetUserId(HttpContext ctx)
    {
        var sub = ctx.User.FindFirstValue(ClaimTypes.NameIdentifier)
               ?? ctx.User.FindFirstValue("sub")
               ?? throw new UnauthorizedAccessException();
        return Guid.Parse(sub);
    }
}
