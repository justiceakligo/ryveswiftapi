namespace RyveSwift.Api.Dtos;

// ─── create-intent ─────────────────────────────────────────────────────────

public record CreatePaymentIntentRequest(Guid QuoteId);

public record PaymentIntentResponse(
    string ClientSecret,
    string PaymentIntentId,
    long Amount,        // in cents (smallest currency unit)
    string Currency,
    string Status);

// ─── status polling ────────────────────────────────────────────────────────

public record PaymentStatusResponse(
    string PaymentIntentId,
    string PaymentStatus,       // Stripe PI status
    string BookingStatus,       // "pending" | "paid" | "label_created" | "failed" | …
    string? RejectionReason,
    Guid? ShipmentId,
    string? TrackingNumber);
