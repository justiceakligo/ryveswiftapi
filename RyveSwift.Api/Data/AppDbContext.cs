using Microsoft.EntityFrameworkCore;
using RyveSwift.Api.Entities;

namespace RyveSwift.Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<UserRefreshToken> UserRefreshTokens => Set<UserRefreshToken>();
    public DbSet<Address> Addresses => Set<Address>();
    public DbSet<Quote> Quotes => Set<Quote>();
    public DbSet<Shipment> Shipments => Set<Shipment>();
    public DbSet<ShipmentPackage> ShipmentPackages => Set<ShipmentPackage>();
    public DbSet<CustomsItem> CustomsItems => Set<CustomsItem>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<MarkupRule> MarkupRules => Set<MarkupRule>();
    public DbSet<ShipmentEvent> ShipmentEvents => Set<ShipmentEvent>();
    public DbSet<AppConfig> AppConfigs => Set<AppConfig>();
    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Users
        modelBuilder.Entity<User>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Email).IsUnique();
            e.Property(x => x.Role).HasDefaultValue("Customer");
        });

        // UserRefreshToken
        modelBuilder.Entity<UserRefreshToken>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasOne(x => x.User).WithMany(u => u.RefreshTokens)
                .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        // PasswordResetToken
        modelBuilder.Entity<PasswordResetToken>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasIndex(x => new { x.UserId, x.UsedAt, x.ExpiresAt });
            e.HasOne(x => x.User).WithMany()
                .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        // Addresses
        modelBuilder.Entity<Address>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasOne(x => x.User).WithMany(u => u.Addresses)
                .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.SetNull);
        });

        // Quotes
        modelBuilder.Entity<Quote>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.RawDhlRateResponse).HasColumnType("jsonb");
            e.HasOne(x => x.User).WithMany(u => u.Quotes)
                .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.SetNull);
        });

        // Shipments
        modelBuilder.Entity<Shipment>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.TrackingNumber).IsUnique().HasFilter("\"TrackingNumber\" IS NOT NULL");
            e.Property(x => x.RawDhlShipmentResponse).HasColumnType("jsonb");
            e.HasOne(x => x.User).WithMany(u => u.Shipments)
                .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.Quote).WithMany()
                .HasForeignKey(x => x.QuoteId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.SenderAddress).WithMany()
                .HasForeignKey(x => x.SenderAddressId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.ReceiverAddress).WithMany()
                .HasForeignKey(x => x.ReceiverAddressId).OnDelete(DeleteBehavior.SetNull);
        });

        // ShipmentPackages
        modelBuilder.Entity<ShipmentPackage>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasOne(x => x.Shipment).WithMany(s => s.Packages)
                .HasForeignKey(x => x.ShipmentId).OnDelete(DeleteBehavior.Cascade);
        });

        // CustomsItems
        modelBuilder.Entity<CustomsItem>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasOne(x => x.Shipment).WithMany(s => s.CustomsItems)
                .HasForeignKey(x => x.ShipmentId).OnDelete(DeleteBehavior.Cascade);
        });

        // Payments
        modelBuilder.Entity<Payment>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.StripePaymentIntentId).IsUnique();
            e.HasOne(x => x.Shipment).WithMany(s => s.Payments)
                .HasForeignKey(x => x.ShipmentId).OnDelete(DeleteBehavior.SetNull);
        });

        // MarkupRules
        modelBuilder.Entity<MarkupRule>(e =>
        {
            e.HasKey(x => x.Id);
        });

        // ShipmentEvents
        modelBuilder.Entity<ShipmentEvent>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.RawPayload).HasColumnType("jsonb");
            e.HasOne(x => x.Shipment).WithMany(s => s.Events)
                .HasForeignKey(x => x.ShipmentId).OnDelete(DeleteBehavior.Cascade);
        });

        // AppConfig
        modelBuilder.Entity<AppConfig>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Key).IsUnique();
        });
    }
}
