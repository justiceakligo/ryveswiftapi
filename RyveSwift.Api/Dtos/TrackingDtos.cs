namespace RyveSwift.Api.Dtos;

public record TrackingResponse(
    string TrackingNumber,
    string Status,
    string? EstimatedDelivery,
    List<TrackingEventResponse> Events);

public record TrackingEventResponse(
    DateTime Timestamp,
    string? Location,
    string? Description);
