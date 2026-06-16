namespace RyveSwift.Api.Dtos;

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
    string ShipmentType,
    int Pieces,
    decimal WeightKg,
    QuoteDimensionsInput DimensionsCm,
    QuoteCustomsInput? Customs,
    string? Incoterm = null);

public record QuoteDeliveryOptionRequest(
    bool Enabled,
    RyvePoolAddressInput? Pickup,
    RyvePoolAddressInput? Dropoff,
    string? DispatchTiming,
    DateTime? ScheduledFor,
    string? DhlPointId,
    string? DhlPointName,
    string? ExternalBranchId,
    string? DispatchMode,
    string? PackageType,
    decimal? WeightKg,
    string? RegionCode,
    string? VehicleCategoryId,
    string? DriverInstructions);

public record EtaBusinessDays(int Min, int Max);

public record QuoteBreakdown(
    decimal Base,
    decimal FuelSurcharge,
    decimal RyveFee,
    decimal DeliveryFee,
    decimal Total);

public record QuoteDeliveryPoint(
    string? Id,
    string? Name,
    string? Address,
    decimal? Lat,
    decimal? Lng);

public record QuoteDeliveryOptionResponse(
    bool Enabled,
    string Status,
    string DispatchTiming,
    DateTime? ScheduledFor,
    string Currency,
    long FeeMinor,
    decimal FeeAmount,
    string? PackageType,
    string? RegionCode,
    string? DispatchMode,
    QuoteDeliveryPoint? Pickup,
    QuoteDeliveryPoint? Dropoff);

public record QuoteResponse(
    Guid QuoteId,
    string Service,
    string Currency,
    decimal Amount,
    EtaBusinessDays EtaBusinessDays,
    DateTime ExpiresAt,
    QuoteBreakdown? Breakdown,
    QuoteDeliveryOptionResponse? DeliveryOption = null,
    bool Expired = false);
