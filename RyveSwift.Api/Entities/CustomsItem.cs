namespace RyveSwift.Api.Entities;

public class CustomsItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ShipmentId { get; set; }
    public string Description { get; set; } = "";
    public decimal Quantity { get; set; }
    public string UnitOfMeasurement { get; set; } = "PCS";
    public decimal UnitPrice { get; set; }
    public string Currency { get; set; } = "CAD";
    public string HsCode { get; set; } = "";
    public string ManufacturerCountry { get; set; } = "";
    public decimal NetWeightKg { get; set; }
    public decimal GrossWeightKg { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Shipment? Shipment { get; set; }
}
