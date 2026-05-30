namespace RyveSwift.Api.Entities;

public class Address
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? UserId { get; set; }
    public string ContactName { get; set; } = "";
    public string? CompanyName { get; set; }
    public string? Email { get; set; }
    public string Phone { get; set; } = "";
    public string CountryCode { get; set; } = "";
    public string CityName { get; set; } = "";
    public string? PostalCode { get; set; }
    public string AddressLine1 { get; set; } = "";
    public string? AddressLine2 { get; set; }
    public string? AddressLine3 { get; set; }
    public bool IsDefaultSender { get; set; } = false;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public User? User { get; set; }
}
