namespace RyveSwift.Api.Entities;

public class ShipmentEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ShipmentId { get; set; }
    public string EventType { get; set; } = "";
    public string? Description { get; set; }
    public string? RawPayload { get; set; }
    public string? Location { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Shipment? Shipment { get; set; }
}
