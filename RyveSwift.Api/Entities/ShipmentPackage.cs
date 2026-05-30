namespace RyveSwift.Api.Entities;

public class ShipmentPackage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ShipmentId { get; set; }
    public decimal WeightKg { get; set; }
    public decimal LengthCm { get; set; }
    public decimal WidthCm { get; set; }
    public decimal HeightCm { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Shipment? Shipment { get; set; }
}
