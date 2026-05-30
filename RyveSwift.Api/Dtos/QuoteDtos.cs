namespace RyveSwift.Api.Dtos;

// ─── Request ───────────────────────────────────────────────────────────────

public record QuoteAddressInput(
    string Country,
    string? PostalCode,
    string? City = null);

public record QuoteDimensionsInput(
    decimal Length,
    decimal Width,
    decimal Height);

public record QuoteCustomsInput(
    string? Category,
    decimal? DeclaredValue,
    string? Currency,
    string? Reason);

public record QuoteRequest(
    QuoteAddressInput Origin,
    QuoteAddressInput Destination,
    string ShipmentType,          // "parcel" | "documents"
    int Pieces,
    decimal WeightKg,
    QuoteDimensionsInput DimensionsCm,
    QuoteCustomsInput? Customs);

// ─── Response ──────────────────────────────────────────────────────────────

public record EtaBusinessDays(int Min, int Max);

public record QuoteBreakdown(
    decimal Base,             // shipping cost after markup (hides our discount)
    decimal FuelSurcharge,    // always 0 — already embedded in Base
    decimal RyveFee);         // flat platform fee

public record QuoteResponse(
    Guid QuoteId,
    string Service,
    string Currency,
    decimal Amount,
    EtaBusinessDays EtaBusinessDays,
    DateTime ExpiresAt,
    QuoteBreakdown? Breakdown,
    bool Expired = false);
