using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using RyveSwift.Api.Common;
using RyveSwift.Api.Data;
using RyveSwift.Api.Dtos;
using RyveSwift.Api.Entities;
using RyveSwift.Api.Services;

namespace RyveSwift.Api.Endpoints;

public static class QuoteEndpoints
{
    // v1: {CA, US} → {GH, NG} only
    private static readonly HashSet<string> ValidOrigins      = new(StringComparer.OrdinalIgnoreCase) { "CA", "US" };
    private static readonly HashSet<string> ValidDestinations = new(StringComparer.OrdinalIgnoreCase) { "GH", "NG" };
    private static readonly HashSet<string> ValidCustomsCurrencies = new(StringComparer.OrdinalIgnoreCase) { "CAD", "USD", "GHS", "NGN" };

    public static void MapQuoteEndpoints(this WebApplication app)
    {
        // POST is public — guests can get a quote before signing in
        app.MapPost("/api/quotes", CreateQuote)
            .WithTags("Quotes")
            .WithName("CreateQuote")
            .WithSummary("Get a shipping rate (public)")
            .RequireRateLimiting("quotes")
            .AllowAnonymous();

        // GET requires auth (booking page reads the quote for the order summary)
        app.MapGet("/api/quotes/{id:guid}", GetQuote)
            .WithTags("Quotes")
            .WithName("GetQuote")
            .WithSummary("Re-read a saved quote")
            .RequireAuthorization();
    }

    // ─── POST /api/quotes ──────────────────────────────────────────────────

    private static async Task<IResult> CreateQuote(
        QuoteRequest req, HttpContext ctx,
        AppDbContext db, DhlService dhl, MarkupService markup, ConfigService config)
    {
        var errors = new List<FieldError>();

        // --- Country / route ---
        if (req.Origin is null || string.IsNullOrWhiteSpace(req.Origin.Country) || req.Origin.Country.Length != 2)
            errors.Add(new("origin.country", "Valid 2-letter origin country code is required."));
        if (req.Destination is null || string.IsNullOrWhiteSpace(req.Destination.Country) || req.Destination.Country.Length != 2)
            errors.Add(new("destination.country", "Valid 2-letter destination country code is required."));

        var originCountry = req.Origin?.Country?.ToUpperInvariant() ?? "";
        var destCountry   = req.Destination?.Country?.ToUpperInvariant() ?? "";

        if (errors.Count == 0)
        {
            if (!ValidOrigins.Contains(originCountry))
                errors.Add(new("origin.country", "v1 only supports shipments originating from CA or US."));
            if (!ValidDestinations.Contains(destCountry))
                errors.Add(new("destination.country", "v1 only supports shipments destined for GH or NG."));
        }

        // --- Pieces ---
        if (req.Pieces < 1 || req.Pieces > 50)
            errors.Add(new("pieces", "Pieces must be between 1 and 50."));

        // --- Weight ---
        if (req.WeightKg < 0.1m || req.WeightKg > 70m)
            errors.Add(new("weightKg", "Must be between 0.1 and 70 kg."));

        // --- Dimensions ---
        if (req.DimensionsCm is null)
        {
            errors.Add(new("dimensionsCm", "Dimensions are required."));
        }
        else
        {
            if (req.DimensionsCm.Length < 1)  errors.Add(new("dimensionsCm.length", "Min 1 cm."));
            if (req.DimensionsCm.Width  < 1)  errors.Add(new("dimensionsCm.width",  "Min 1 cm."));
            if (req.DimensionsCm.Height < 1)  errors.Add(new("dimensionsCm.height", "Min 1 cm."));
            if (req.DimensionsCm.Length > 120) errors.Add(new("dimensionsCm.length", "Max 120 cm."));
            if (req.DimensionsCm.Width  > 80)  errors.Add(new("dimensionsCm.width",  "Max 80 cm."));
            if (req.DimensionsCm.Height > 80)  errors.Add(new("dimensionsCm.height", "Max 80 cm."));

            var girth = 2 * req.DimensionsCm.Width + 2 * req.DimensionsCm.Height + req.DimensionsCm.Length;
            if (girth > 300)
                errors.Add(new("dimensionsCm", $"Girth (2W+2H+L) must be ≤ 300 cm (got {girth} cm)."));
        }

        // --- Customs (for parcels) ---
        var productCode = req.ShipmentType?.Equals("documents", StringComparison.OrdinalIgnoreCase) == true ? "D" : "P";
        if (productCode == "P" && req.Customs is not null)
        {
            if (req.Customs.DeclaredValue.HasValue && req.Customs.DeclaredValue.Value <= 0)
                errors.Add(new("customs.declaredValue", "Must be greater than 0."));
            if (!string.IsNullOrWhiteSpace(req.Customs.Currency) &&
                !ValidCustomsCurrencies.Contains(req.Customs.Currency))
                errors.Add(new("customs.currency", $"Currency must be one of: {string.Join(", ", ValidCustomsCurrencies)}."));
        }

        if (errors.Count > 0)
            return Results.BadRequest(new ApiError("validation_failed", "Some shipment details are invalid.", errors));

        // ── Call DHL ─────────────────────────────────────────────────────────
        // Use city if provided; fall back to postalCode; then fall back to major city per country.
        // DHL requires cityName — never send null.
        var originCity = req.Origin!.City
            ?? req.Origin.PostalCode
            ?? DefaultCity(originCountry);
        var destCity = req.Destination!.City
            ?? req.Destination.PostalCode
            ?? DefaultCity(destCountry);

        try
        {
            var (baseRate, dhlCurrency, rawJson) = await dhl.GetRateAsync(
                originCountry, originCity, req.Origin.PostalCode,
                destCountry,   destCity,   req.Destination.PostalCode,
                req.WeightKg,
                req.DimensionsCm!.Length, req.DimensionsCm.Width, req.DimensionsCm.Height,
                productCode);

            var (markupPct, platformFee) = await markup.GetMarkupAsync(
                originCountry, destCountry, req.WeightKg, productCode);

            var shippingSubtotal = Math.Round(baseRate * (1 + markupPct / 100m), 2);
            var totalAmount      = shippingSubtotal + platformFee;
            var expiryHours      = config.GetInt("QUOTE_EXPIRY_HOURS", 24);
            var currency         = config.Get("DEFAULT_CURRENCY", "CAD");

            var userId = TryGetUserId(ctx);

            var quote = new Quote
            {
                UserId                = userId,
                OriginCountry         = originCountry,
                DestinationCountry    = destCountry,
                OriginCity            = originCity ?? "",
                OriginPostalCode      = req.Origin.PostalCode?.Trim(),
                DestinationCity       = destCity ?? "",
                DestinationPostalCode = req.Destination.PostalCode?.Trim(),
                ProductCode           = productCode,
                Pieces                = req.Pieces,
                WeightKg              = req.WeightKg,
                LengthCm              = req.DimensionsCm.Length,
                WidthCm               = req.DimensionsCm.Width,
                HeightCm              = req.DimensionsCm.Height,
                DhlBaseRate           = baseRate,
                DhlCurrency           = dhlCurrency,
                MarkupPercent         = markupPct,
                PlatformFee           = platformFee,
                TotalAmount           = totalAmount,
                Currency              = currency,
                RawDhlRateResponse    = rawJson,
                ExpiresAt             = DateTime.UtcNow.AddHours(expiryHours),
                CustomsCategory       = req.Customs?.Category,
                CustomsDeclaredValue  = req.Customs?.DeclaredValue,
                CustomsCurrency       = req.Customs?.Currency,
                CustomsReason         = req.Customs?.Reason,
            };

            db.Quotes.Add(quote);
            await db.SaveChangesAsync();

            return Results.Created($"/api/quotes/{quote.Id}", BuildResponse(quote, shippingSubtotal, platformFee));
        }
        catch (DhlException ex)
        {
            return Results.Json(new ApiError(ex.ErrorCode, ex.Message),
                statusCode: ex.ErrorCode == "UNSUPPORTED_ROUTE" ? 422 : 503);
        }
    }

    // ─── GET /api/quotes/{id} ──────────────────────────────────────────────

    private static async Task<IResult> GetQuote(Guid id, HttpContext ctx, AppDbContext db)
    {
        var userId = TryGetUserId(ctx);

        // Allow owner or admin; anonymous quote (userId null) is accessible to anyone who knows the ID
        var quote = userId.HasValue
            ? await db.Quotes.FirstOrDefaultAsync(q => q.Id == id && (q.UserId == userId || q.UserId == null))
            : await db.Quotes.FindAsync(id);

        if (quote is null) return Results.NotFound(new ApiError("not_found", "Quote not found."));

        var expired          = quote.ExpiresAt < DateTime.UtcNow;
        var shippingSubtotal = Math.Round(quote.DhlBaseRate * (1 + quote.MarkupPercent / 100m), 2);
        return Results.Ok(BuildResponse(quote, shippingSubtotal, quote.PlatformFee, expired));
    }

    // ─── Helpers ──────────────────────────────────────────────────────────

    private static QuoteResponse BuildResponse(Quote q, decimal shippingSubtotal, decimal ryveFee, bool expired = false)
    {
        var service = q.ProductCode.Equals("D", StringComparison.OrdinalIgnoreCase)
            ? "DHL Express Documents"
            : "DHL Express Worldwide";

        var eta = q.ProductCode.Equals("D", StringComparison.OrdinalIgnoreCase)
            ? new EtaBusinessDays(2, 3)
            : new EtaBusinessDays(3, 5);

        var breakdown = expired ? null : new QuoteBreakdown(shippingSubtotal, 0m, ryveFee);

        return new QuoteResponse(q.Id, service, q.Currency, q.TotalAmount, eta, q.ExpiresAt, breakdown, expired);
    }

    private static Guid? TryGetUserId(HttpContext ctx)
    {
        var sub = ctx.User.FindFirstValue(ClaimTypes.NameIdentifier)
               ?? ctx.User.FindFirstValue("sub");
        return sub is null ? null : Guid.TryParse(sub, out var id) ? id : null;
    }

    private static string DefaultCity(string countryCode) => countryCode.ToUpperInvariant() switch
    {
        "CA" => "Toronto",
        "US" => "New York",
        "GH" => "Accra",
        "NG" => "Lagos",
        _    => "Unknown"
    };
}
