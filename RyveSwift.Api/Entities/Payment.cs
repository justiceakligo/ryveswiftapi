namespace RyveSwift.Api.Entities;

public class Payment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? ShipmentId { get; set; }
    /// <summary>Set when the PI is created against a quote (before shipment exists).</summary>
    public Guid? QuoteId { get; set; }
    /// <summary>Client-supplied Idempotency-Key header value.</summary>
    public string? IdempotencyKey { get; set; }
    public string StripePaymentIntentId { get; set; } = "";
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "CAD";
    public string Status { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public Shipment? Shipment { get; set; }
}
