namespace RyveSwift.Api.Entities;

public class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Email { get; set; } = "";
    public string? Phone { get; set; }
    public string? FullName { get; set; }
    public string PasswordHash { get; set; } = "";
    public string Role { get; set; } = "Customer";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastLogin { get; set; }

    public ICollection<Address> Addresses { get; set; } = new List<Address>();
    public ICollection<Quote> Quotes { get; set; } = new List<Quote>();
    public ICollection<Shipment> Shipments { get; set; } = new List<Shipment>();
    public ICollection<UserRefreshToken> RefreshTokens { get; set; } = new List<UserRefreshToken>();
}
