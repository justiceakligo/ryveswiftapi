namespace RyveSwift.Api.Dtos;

public record AdminShipmentResponse(
    Guid Id, Guid? UserId, string? UserEmail,
    string? TrackingNumber, string Status,
    string OriginCountry, string DestinationCountry,
    decimal TotalAmount, string Currency, DateTime CreatedAt);

public record AdminUserResponse(
    Guid Id, string Email, string? FullName, string? Phone,
    string Role, DateTime CreatedAt, DateTime? LastLogin);

public record RevenueReportResponse(
    decimal TotalRevenue, int TotalShipments, int PaidShipments,
    string Currency, DateTime From, DateTime To);

public record MarkupRuleRequest(
    string? OriginCountry,
    string? DestinationCountry,
    decimal? MinWeightKg,
    decimal? MaxWeightKg,
    string? ProductCode,
    decimal MarkupPercent,
    decimal PlatformFee);

public record MarkupRuleResponse(
    Guid Id,
    string? OriginCountry,
    string? DestinationCountry,
    decimal? MinWeightKg,
    decimal? MaxWeightKg,
    string? ProductCode,
    decimal MarkupPercent,
    decimal PlatformFee,
    bool IsActive,
    DateTime CreatedAt);
