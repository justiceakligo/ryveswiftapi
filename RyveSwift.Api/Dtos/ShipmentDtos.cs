namespace RyveSwift.Api.Dtos;

// ─── Booking confirm request ────────────────────────────────────────────────

public record ConfirmBookingRequest(
    string PaymentIntentId,
    Guid QuoteId,
    Guid? SenderAddressId,
    Guid ReceiverAddressId,
    List<CustomsItemRequest>? CustomsItems,
    string? ExportReason,
    string? InvoiceNumber,
    DateTime? InvoiceDate,
    string? Incoterm = null);

// ─── Shared customs / package sub-shapes ───────────────────────────────────

public record CustomsItemRequest(
    string Description,
    decimal Quantity,
    string? UnitOfMeasurement,
    decimal UnitPrice,
    string? Currency,
    string? HsCode,
    string? ManufacturerCountry,
    decimal? NetWeightKg,
    decimal? GrossWeightKg);

public record PackageResponse(Guid Id, decimal WeightKg, decimal LengthCm, decimal WidthCm, decimal HeightCm);

public record CustomsItemResponse(
    Guid Id, string Description, decimal Quantity, string UnitOfMeasurement,
    decimal UnitPrice, string Currency, string HsCode, string ManufacturerCountry,
    decimal NetWeightKg, decimal GrossWeightKg);

// ─── Booking confirm response ───────────────────────────────────────────────

public record DocumentInfo(string Type, string Url, bool Ready);

public record BookingConfirmResponse(
    Guid ShipmentId,
    string? TrackingNumber,
    string Status,
    List<DocumentInfo> Documents,
    RyvePoolDeliveryResponse? OrderDelivery = null,
    string? RefundId = null);

// ─── Shipment list (GET /api/shipments) ─────────────────────────────────────

public record ShipmentListItem(
    Guid Id,
    DateTime CreatedAt,
    string Service,
    string Route,
    decimal WeightKg,
    decimal Amount,
    string Currency,
    string Status,
    string? TrackingNumber);

public record ShipmentListResponse(List<ShipmentListItem> Shipments);

// ─── Shipment detail (GET /api/shipments/:id) ───────────────────────────────

public record ShipmentPaymentInfo(string Status, string? PaymentIntentId);

public record ShipmentDetailResponse(
    Guid Id,
    string Status,
    string? TrackingNumber,
    List<DocumentInfo> Documents,
    AddressResponse? Sender,
    AddressResponse? Receiver,
    List<CustomsItemResponse> Customs,
    ShipmentPaymentInfo? Payment,
    decimal TotalAmount,
    string Currency,
    DateTime CreatedAt,
    RyvePoolDeliveryResponse? OrderDelivery = null);

// ─── Legacy internal shapes (used by admin / create-label endpoints) ─────────

public record CreateShipmentFromQuoteRequest(
    Guid QuoteId,
    Guid SenderAddressId,
    Guid ReceiverAddressId,
    List<CustomsItemRequest> CustomsItems,
    string ExportReason,
    string InvoiceNumber,
    DateTime InvoiceDate,
    string? Incoterm = null);

public record ShipmentResponse(
    Guid Id,
    Guid? UserId,
    string? TrackingNumber,
    string Status,
    string OriginCountry,
    string DestinationCountry,
    string ProductCode,
    decimal TotalAmount,
    string Currency,
    string? LabelUrl,
    string? InvoiceUrl,
    string? WaybillUrl,
    DateTime CreatedAt,
    DateTime UpdatedAt);
