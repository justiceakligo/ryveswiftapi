using Microsoft.EntityFrameworkCore;
using RyveSwift.Api.Data;
using RyveSwift.Api.Entities;

namespace RyveSwift.Api.Services;

public class MarkupService
{
    private readonly AppDbContext _db;
    private readonly ConfigService _config;

    public MarkupService(AppDbContext db, ConfigService config)
    {
        _db = db;
        _config = config;
    }

    public async Task<(decimal markupPercent, decimal platformFee)> GetMarkupAsync(
        string originCountry, string destinationCountry, decimal weightKg, string productCode)
    {
        // Find the most specific matching rule (exact countries > wildcard)
        var rules = await _db.MarkupRules
            .Where(r => r.IsActive)
            .ToListAsync();

        var bestRule = rules
            .Where(r =>
                (r.OriginCountry == null || r.OriginCountry.Equals(originCountry, StringComparison.OrdinalIgnoreCase)) &&
                (r.DestinationCountry == null || r.DestinationCountry.Equals(destinationCountry, StringComparison.OrdinalIgnoreCase)) &&
                (r.ProductCode == null || r.ProductCode.Equals(productCode, StringComparison.OrdinalIgnoreCase)) &&
                (r.MinWeightKg == null || weightKg >= r.MinWeightKg) &&
                (r.MaxWeightKg == null || weightKg <= r.MaxWeightKg))
            .OrderByDescending(r => (r.OriginCountry != null ? 4 : 0) +
                                    (r.DestinationCountry != null ? 2 : 0) +
                                    (r.ProductCode != null ? 1 : 0))
            .FirstOrDefault();

        if (bestRule is not null)
            return (bestRule.MarkupPercent, bestRule.PlatformFee);

        // Fall back to DB defaults
        var defaultMarkup = _config.GetDecimal("DEFAULT_MARKUP_PERCENT", 20m);
        var defaultFee = _config.GetDecimal("DEFAULT_PLATFORM_FEE", 5m);
        return (defaultMarkup, defaultFee);
    }

    public static decimal ApplyMarkup(decimal baseRate, decimal markupPercent, decimal platformFee)
    {
        return Math.Round(baseRate * (1 + markupPercent / 100m) + platformFee, 2);
    }
}
