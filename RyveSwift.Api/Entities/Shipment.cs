namespace RyveSwift.Api.Entities;

public class Shipment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? UserId { get; set; }
    public Guid? QuoteId { get; set; }
    public Guid? SenderAddressId { get; set; }
    public Guid? ReceiverAddressId { get; set; }
    public string? DhlShipmentId { get; set; }
    public string? TrackingNumber { get; set; }
    public string ProductCode { get; set; } = "P";
    public string Incoterm { get; set; } = "DAP";
    public string OriginCountry { get; set; } = "";
    public string DestinationCountry { get; set; } = "";
    public string Status { get; set; } = "PendingPayment";
    public decimal? DhlBaseRate { get; set; }
    public decimal? MarkupPercent { get; set; }
    public decimal? PlatformFee { get; set; }
    public decimal TotalAmount { get; set; }
    public string Currency { get; set; } = "CAD";
    public string? LabelFilePath { get; set; }
    public string? InvoiceFilePath { get; set; }
    public string? WaybillFilePath { get; set; }
    public string? RawDhlShipmentResponse { get; set; }
    public string? ExportReason { get; set; }
    public string? InvoiceNumber { get; set; }
    public DateTime? InvoiceDate { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public User? User { get; set; }
    public Quote? Quote { get; set; }
    public Address? SenderAddress { get; set; }
    public Address? ReceiverAddress { get; set; }
    public ICollection<ShipmentPackage> Packages { get; set; } = new List<ShipmentPackage>();
    public ICollection<CustomsItem> CustomsItems { get; set; } = new List<CustomsItem>();
    public ICollection<Payment> Payments { get; set; } = new List<Payment>();
    public ICollection<ShipmentEvent> Events { get; set; } = new List<ShipmentEvent>();
}
