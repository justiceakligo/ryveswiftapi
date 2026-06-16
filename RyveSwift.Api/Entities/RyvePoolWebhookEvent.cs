namespace RyveSwift.Api.Entities;

public class RyvePoolWebhookEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? DeliveryId { get; set; }
    public string? RyvePoolEventId { get; set; }
    public string Event { get; set; } = "";
    public string Environment { get; set; } = "";
    public string? DispatchId { get; set; }
    public string? ExternalOrderId { get; set; }
    public string? PreviousStatus { get; set; }
    public string? Status { get; set; }
    public string? SignatureHeader { get; set; }
    public string? DeliveryHeader { get; set; }
    public bool IsSignatureValid { get; set; }
    public string RawPayload { get; set; } = "";
    public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;

    public RyvePoolDelivery? Delivery { get; set; }
}
