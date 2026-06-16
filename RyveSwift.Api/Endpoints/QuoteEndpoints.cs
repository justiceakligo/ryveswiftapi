using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RyveSwift.Api.Common;
using RyveSwift.Api.Data;
using RyveSwift.Api.Dhl;
using RyveSwift.Api.Dtos;
using RyveSwift.Api.Entities;
using RyveSwift.Api.Services;

namespace RyveSwift.Api.Endpoints;

public static class QuoteEndpoints
{
    public static void MapQuoteEndpoints(this WebApplication app)
    {
        app.MapPost("/api/quotes", CreateQuote)
            .WithTags("Quotes")
            .WithName("CreateQuote")
            .WithSummary("Get a shipping rate (public)")
            .RequireRateLimiting("quotes")
            .AllowAnonymous();

        app.MapGet("/api/quotes/{id:guid}", GetQuote)
            .WithTags("Quotes")
            .WithName("GetQuote")
            .WithSummary("Re-read a saved quote")
            .RequireAuthorization();

        app.MapPost("/api/quotes/{id:guid}/delivery-option", UpsertDeliveryOption)
            .WithTags("Quotes")
            .WithName("UpsertQuoteDeliveryOption")
            .WithSummary("Attach or remove optional RyvePool pickup/delivery to a saved quote before payment")
            .RequireAuthorization();
    }

    private static async Task<IResult> CreateQuote(
        QuoteRequest req,
        HttpContext ctx,
        AppDbContext db,
        DhlService dhl,
        MarkupService markup,
        ConfigService config)
    {
        var errors = new List<FieldError>();

        if (req.Origin is null || string.IsNullOrWhiteSpace(req.Origin.Country) || req.Origin.Country.Length != 2)
            errors.Add(new("origin.country", "Valid 2-letter origin country code is required."));
        if (req.Destination is null || string.IsNullOrWhiteSpace(req.Destination.Country) || req.Destination.Country.Length != 2)
            errors.Add(new("destination.country", "Valid 2-letter destination country code is required."));

        var originCountry = req.Origin?.Country?.ToUpperInvariant() ?? "";
        var destCountry = req.Destination?.Country?.ToUpperInvariant() ?? "";

        if (RequiresPostalCodeForDhlRate(originCountry) && string.IsNullOrWhiteSpace(req.Origin?.PostalCode))
            errors.Add(new("origin.postalCode", "A postal code is required for DHL rates from this origin country."));

        if (RequiresPostalCodeForDhlRate(destCountry) && string.IsNullOrWhiteSpace(req.Destination?.PostalCode))
            errors.Add(new("destination.postalCode", "A postal code or DHL-recognized service-area code is required for DHL rates to this destination country."));

        if (req.Pieces < 1 || req.Pieces > 50)
            errors.Add(new("pieces", "Pieces must be between 1 and 50."));

        if (req.WeightKg < 0.1m || req.WeightKg > 70m)
            errors.Add(new("weightKg", "Must be between 0.1 and 70 kg."));

        if (req.DimensionsCm is null)
        {
            errors.Add(new("dimensionsCm", "Dimensions are required."));
        }
        else
        {
            if (req.DimensionsCm.Length < 1) errors.Add(new("dimensionsCm.length", "Min 1 cm."));
            if (req.DimensionsCm.Width < 1) errors.Add(new("dimensionsCm.width", "Min 1 cm."));
            if (req.DimensionsCm.Height < 1) errors.Add(new("dimensionsCm.height", "Min 1 cm."));
            if (req.DimensionsCm.Length > 120) errors.Add(new("dimensionsCm.length", "Max 120 cm."));
            if (req.DimensionsCm.Width > 80) errors.Add(new("dimensionsCm.width", "Max 80 cm."));
            if (req.DimensionsCm.Height > 80) errors.Add(new("dimensionsCm.height", "Max 80 cm."));

            var girth = 2 * req.DimensionsCm.Width + 2 * req.DimensionsCm.Height + req.DimensionsCm.Length;
            if (girth > 300)
                errors.Add(new("dimensionsCm", $"Girth (2W+2H+L) must be <= 300 cm (got {girth} cm)."));
        }

        var isInternational = !originCountry.Equals(destCountry, StringComparison.OrdinalIgnoreCase);
        var isDocuments = req.ShipmentType?.Equals("documents", StringComparison.OrdinalIgnoreCase) == true;
        var productCode = "P";
        var incoterm = NormalizeIncoterm(req.Incoterm);

        if (isDocuments)
            errors.Add(new("shipmentType", "Only parcel shipments using DHL Express Worldwide are supported by this certified integration."));

        if (!isInternational)
            errors.Add(new("destination.country", DhlProductPolicy.HiddenServiceMessage("DHL Domestic Express")));

        if (incoterm == "DDP" && !isInternational)
            errors.Add(new("incoterm", "DDP can only be used for international shipments."));

        if (productCode == "P" && req.Customs is not null)
        {
            if (req.Customs.DeclaredValue.HasValue && req.Customs.DeclaredValue.Value <= 0)
                errors.Add(new("customs.declaredValue", "Must be greater than 0."));
            if (!string.IsNullOrWhiteSpace(req.Customs.Currency) && !IsValidCurrency(req.Customs.Currency))
                errors.Add(new("customs.currency", "Currency must be a valid 3-letter ISO currency code."));
        }

        if (errors.Count > 0)
            return Results.BadRequest(new ApiError("validation_failed", "Some shipment details are invalid.", errors));

        var originCity = req.Origin!.City ?? req.Origin.PostalCode ?? DefaultCity(originCountry);
        var destCity = req.Destination!.City ?? req.Destination.PostalCode ?? DefaultCity(destCountry);

        try
        {
            var (baseRate, dhlCurrency, rawJson) = await dhl.GetRateAsync(
                originCountry, originCity, req.Origin.PostalCode,
                destCountry, destCity, req.Destination.PostalCode,
                req.WeightKg,
                req.DimensionsCm!.Length, req.DimensionsCm.Width, req.DimensionsCm.Height,
                productCode,
                req.Customs?.DeclaredValue,
                req.Customs?.Currency);

            var (markupPct, platformFee) = await markup.GetMarkupAsync(
                originCountry, destCountry, req.WeightKg, productCode);

            var shippingSubtotal = Math.Round(baseRate * (1 + markupPct / 100m), 2);
            var totalAmount = shippingSubtotal + platformFee;
            var expiryHours = config.GetInt("QUOTE_EXPIRY_HOURS", 24);
            var currency = config.Get("DEFAULT_CURRENCY", "CAD");

            var quote = new Quote
            {
                UserId = TryGetUserId(ctx),
                OriginCountry = originCountry,
                DestinationCountry = destCountry,
                OriginCity = originCity ?? "",
                OriginPostalCode = req.Origin.PostalCode?.Trim(),
                DestinationCity = destCity ?? "",
                DestinationPostalCode = req.Destination.PostalCode?.Trim(),
                ProductCode = productCode,
                Incoterm = incoterm,
                Pieces = req.Pieces,
                WeightKg = req.WeightKg,
                LengthCm = req.DimensionsCm.Length,
                WidthCm = req.DimensionsCm.Width,
                HeightCm = req.DimensionsCm.Height,
                DhlBaseRate = baseRate,
                DhlCurrency = dhlCurrency,
                MarkupPercent = markupPct,
                PlatformFee = platformFee,
                TotalAmount = totalAmount,
                Currency = currency,
                RawDhlRateResponse = rawJson,
                ExpiresAt = DateTime.UtcNow.AddHours(expiryHours),
                CustomsCategory = req.Customs?.Category,
                CustomsDeclaredValue = req.Customs?.DeclaredValue,
                CustomsCurrency = req.Customs?.Currency,
                CustomsReason = req.Customs?.Reason,
            };

            db.Quotes.Add(quote);
            await db.SaveChangesAsync();

            return Results.Created($"/api/quotes/{quote.Id}", BuildResponse(quote, shippingSubtotal, platformFee));
        }
        catch (DhlException ex)
        {
            return Results.Json(new ApiError(ex.ErrorCode, ex.Message),
                statusCode: ex.IsClientError ||
                    ex.ErrorCode is "UNSUPPORTED_ROUTE" or "UNSUPPORTED_DHL_SERVICE"
                        ? StatusCodes.Status422UnprocessableEntity
                        : StatusCodes.Status503ServiceUnavailable);
        }
    }

    private static async Task<IResult> GetQuote(Guid id, HttpContext ctx, AppDbContext db)
    {
        var userId = TryGetUserId(ctx);

        var quote = userId.HasValue
            ? await db.Quotes.FirstOrDefaultAsync(q => q.Id == id && (q.UserId == userId || q.UserId == null))
            : await db.Quotes.FindAsync(id);

        if (quote is null)
            return Results.NotFound(new ApiError("not_found", "Quote not found."));

        var expired = quote.ExpiresAt < DateTime.UtcNow;
        var shippingSubtotal = GetShippingSubtotal(quote);
        return Results.Ok(BuildResponse(quote, shippingSubtotal, quote.PlatformFee, expired));
    }

    private static async Task<IResult> UpsertDeliveryOption(
        Guid id,
        QuoteDeliveryOptionRequest req,
        HttpContext ctx,
        AppDbContext db,
        RyvePoolService ryvePool,
        CancellationToken ct)
    {
        var userId = GetUserId(ctx);
        var quote = await db.Quotes.FirstOrDefaultAsync(q => q.Id == id, ct);
        if (quote is null || (quote.UserId.HasValue && quote.UserId.Value != userId && !ctx.User.IsInRole("Admin")))
            return Results.NotFound(new ApiError("not_found", "Quote not found."));

        if (quote.ExpiresAt < DateTime.UtcNow)
            return Results.Conflict(new ApiError("quote_expired", "This quote has expired. Please request a new one."));

        if (await HasLockedPaymentAsync(db, quote.Id, ct))
            return Results.Conflict(new ApiError("quote_locked", "This quote already has a payment in progress and can no longer be changed."));

        if (!quote.UserId.HasValue)
            quote.UserId = userId;

        var shippingSubtotal = GetShippingSubtotal(quote);

        if (!req.Enabled)
        {
            ClearDeliveryOption(quote);
            quote.TotalAmount = shippingSubtotal + quote.PlatformFee;
            await db.SaveChangesAsync(ct);
            return Results.Ok(BuildResponse(quote, shippingSubtotal, quote.PlatformFee));
        }

        try
        {
            var cfg = ryvePool.GetRuntimeConfig();
            var packageType = RyvePoolService.NormalizePackageType(req.PackageType ?? cfg.DefaultPackageType);
            var dispatchMode = RyvePoolService.NormalizeDispatchMode(req.DispatchMode ?? cfg.DefaultDispatchMode);
            var dispatchTiming = RyvePoolDispatchCoordinator.NormalizeDispatchTiming(req.DispatchTiming);
            var scheduledForUtc = req.ScheduledFor?.ToUniversalTime();
            var errors = ValidateDeliveryOption(req, dispatchTiming, scheduledForUtc);

            if (errors.Count > 0)
                return Results.BadRequest(new ApiError("validation_failed", "Some delivery option details are invalid.", errors));

            var deliveryQuote = await ryvePool.GetQuoteAsync(new RyvePoolQuoteApiRequest
            {
                PickupLat = req.Pickup!.Lat,
                PickupLng = req.Pickup.Lng,
                DropoffLat = req.Dropoff!.Lat,
                DropoffLng = req.Dropoff.Lng,
                PickupCity = null,
                DropoffCity = null,
                PackageType = packageType,
                WeightKg = req.WeightKg ?? quote.WeightKg,
                RegionCode = Clean(req.RegionCode) ?? cfg.DefaultRegionCode,
                VehicleCategoryId = Clean(req.VehicleCategoryId)
            }, ct);

            var priced = ExtractDeliveryPrice(deliveryQuote.Json, quote.Currency);
            if (!priced.Currency.Equals(quote.Currency, StringComparison.OrdinalIgnoreCase))
            {
                return Results.UnprocessableEntity(new ApiError(
                    "delivery_currency_mismatch",
                    $"RyvePool returned {priced.Currency}, but the quote currency is {quote.Currency}."));
            }

            ApplyDeliveryOption(
                quote,
                req,
                packageType,
                dispatchMode,
                dispatchTiming,
                scheduledForUtc,
                priced,
                deliveryQuote.RawJson,
                cfg.DefaultRegionCode,
                cfg.DefaultExternalBranchId);

            quote.TotalAmount = shippingSubtotal + quote.PlatformFee + MinorToMajor(priced.TotalMinor);
            await db.SaveChangesAsync(ct);

            return Results.Ok(BuildResponse(quote, shippingSubtotal, quote.PlatformFee));
        }
        catch (RyvePoolException ex)
        {
            return RyvePoolError(ex);
        }
    }

    private static List<FieldError> ValidateDeliveryOption(
        QuoteDeliveryOptionRequest req,
        string dispatchTiming,
        DateTime? scheduledForUtc)
    {
        var errors = new List<FieldError>();

        ValidateDeliveryAddress(req.Pickup, "pickup", errors);
        ValidateDeliveryAddress(req.Dropoff, "dropoff", errors);

        if (string.IsNullOrWhiteSpace(req.DhlPointId) && string.IsNullOrWhiteSpace(req.DhlPointName))
            errors.Add(new("dhlPoint", "DHL point ID or name is required for RyvePool dropoff."));

        if (dispatchTiming == RyvePoolDispatchCoordinator.DispatchTimingScheduled)
        {
            if (!scheduledForUtc.HasValue)
                errors.Add(new("scheduledFor", "Scheduled dispatch time is required when dispatchTiming is scheduled."));
            else if (scheduledForUtc.Value <= DateTime.UtcNow)
                errors.Add(new("scheduledFor", "Scheduled dispatch time must be in the future."));
        }

        if (req.WeightKg.HasValue && req.WeightKg.Value <= 0)
            errors.Add(new("weightKg", "Delivery weight must be greater than 0."));

        return errors;
    }

    private static void ValidateDeliveryAddress(RyvePoolAddressInput? address, string prefix, List<FieldError> errors)
    {
        if (address is null)
        {
            errors.Add(new(prefix, $"{prefix} is required."));
            return;
        }

        if (string.IsNullOrWhiteSpace(address.Name))
            errors.Add(new($"{prefix}.name", "Name is required."));
        if (string.IsNullOrWhiteSpace(address.Phone))
            errors.Add(new($"{prefix}.phone", "Phone is required."));
        if (!address.Lat.HasValue || !address.Lng.HasValue)
            errors.Add(new($"{prefix}.coordinates", "Latitude and longitude are required to price the RyvePool delivery option."));
    }

    private static async Task<bool> HasLockedPaymentAsync(AppDbContext db, Guid quoteId, CancellationToken ct) =>
        await db.Payments.AnyAsync(
            p => p.QuoteId == quoteId && (p.Status == "pending" || p.Status == "succeeded"),
            ct);

    private static void ApplyDeliveryOption(
        Quote quote,
        QuoteDeliveryOptionRequest req,
        string packageType,
        string dispatchMode,
        string dispatchTiming,
        DateTime? scheduledForUtc,
        DeliveryPrice priced,
        string rawQuote,
        string defaultRegionCode,
        string? defaultExternalBranchId)
    {
        quote.RyvePoolDeliverySelected = true;
        quote.RyvePoolDeliveryStatus = "quoted";
        quote.RyvePoolDeliveryDispatchTiming = dispatchTiming;
        quote.RyvePoolDeliveryScheduledForUtc = scheduledForUtc;
        quote.RyvePoolDeliveryFeeMinor = priced.TotalMinor;
        quote.RyvePoolDeliveryCurrency = priced.Currency;
        quote.RyvePoolDeliveryQuoteRawResponse = rawQuote;
        quote.RyvePoolPickupName = req.Pickup!.Name.Trim();
        quote.RyvePoolPickupPhone = req.Pickup.Phone.Trim();
        quote.RyvePoolPickupAddress = Clean(req.Pickup.Address);
        quote.RyvePoolPickupLandmark = Clean(req.Pickup.Landmark);
        quote.RyvePoolPickupLat = req.Pickup.Lat;
        quote.RyvePoolPickupLng = req.Pickup.Lng;
        quote.RyvePoolDropoffName = req.Dropoff!.Name.Trim();
        quote.RyvePoolDropoffPhone = req.Dropoff.Phone.Trim();
        quote.RyvePoolDropoffEmail = Clean(req.Dropoff.Email);
        quote.RyvePoolDropoffAddress = Clean(req.Dropoff.Address);
        quote.RyvePoolDropoffLandmark = Clean(req.Dropoff.Landmark);
        quote.RyvePoolDropoffLat = req.Dropoff.Lat;
        quote.RyvePoolDropoffLng = req.Dropoff.Lng;
        quote.RyvePoolDhlPointId = Clean(req.DhlPointId);
        quote.RyvePoolDhlPointName = Clean(req.DhlPointName);
        quote.RyvePoolRegionCode = Clean(req.RegionCode) ?? defaultRegionCode;
        quote.RyvePoolExternalBranchId = Clean(req.ExternalBranchId) ?? defaultExternalBranchId;
        quote.RyvePoolDispatchMode = dispatchMode;
        quote.RyvePoolPackageType = packageType;
        quote.RyvePoolParcelWeightKg = req.WeightKg ?? quote.WeightKg;
        quote.RyvePoolDriverInstructions = Clean(req.DriverInstructions);
        quote.RyvePoolVehicleCategoryId = Clean(req.VehicleCategoryId);
    }

    private static void ClearDeliveryOption(Quote quote)
    {
        quote.RyvePoolDeliverySelected = false;
        quote.RyvePoolDeliveryStatus = null;
        quote.RyvePoolDeliveryDispatchTiming = null;
        quote.RyvePoolDeliveryScheduledForUtc = null;
        quote.RyvePoolDeliveryFeeMinor = 0;
        quote.RyvePoolDeliveryCurrency = null;
        quote.RyvePoolDeliveryQuoteRawResponse = null;
        quote.RyvePoolPickupName = null;
        quote.RyvePoolPickupPhone = null;
        quote.RyvePoolPickupAddress = null;
        quote.RyvePoolPickupLandmark = null;
        quote.RyvePoolPickupLat = null;
        quote.RyvePoolPickupLng = null;
        quote.RyvePoolDropoffName = null;
        quote.RyvePoolDropoffPhone = null;
        quote.RyvePoolDropoffEmail = null;
        quote.RyvePoolDropoffAddress = null;
        quote.RyvePoolDropoffLandmark = null;
        quote.RyvePoolDropoffLat = null;
        quote.RyvePoolDropoffLng = null;
        quote.RyvePoolDhlPointId = null;
        quote.RyvePoolDhlPointName = null;
        quote.RyvePoolRegionCode = null;
        quote.RyvePoolExternalBranchId = null;
        quote.RyvePoolDispatchMode = null;
        quote.RyvePoolPackageType = null;
        quote.RyvePoolParcelWeightKg = null;
        quote.RyvePoolDriverInstructions = null;
        quote.RyvePoolVehicleCategoryId = null;
    }

    private static QuoteResponse BuildResponse(Quote q, decimal shippingSubtotal, decimal ryveFee, bool expired = false)
    {
        var deliveryFee = q.RyvePoolDeliverySelected ? MinorToMajor(q.RyvePoolDeliveryFeeMinor) : 0m;
        var breakdown = expired
            ? null
            : new QuoteBreakdown(shippingSubtotal, 0m, ryveFee, deliveryFee, q.TotalAmount);
        var deliveryOption = expired ? null : BuildDeliveryOption(q);

        if (DhlProductPolicy.TryGetHiddenServiceName(q.ProductCode, null, out _))
            return new QuoteResponse(
                q.Id,
                "DHL service unavailable",
                q.Currency,
                q.TotalAmount,
                new EtaBusinessDays(3, 5),
                q.ExpiresAt,
                breakdown,
                deliveryOption,
                expired);

        var eta = q.ProductCode.ToUpperInvariant() switch
        {
            "N" => new EtaBusinessDays(1, 2),
            "D" => new EtaBusinessDays(2, 3),
            _ => new EtaBusinessDays(3, 5)
        };

        return new QuoteResponse(
            q.Id,
            "DHL Express Worldwide",
            q.Currency,
            q.TotalAmount,
            eta,
            q.ExpiresAt,
            breakdown,
            deliveryOption,
            expired);
    }

    private static QuoteDeliveryOptionResponse? BuildDeliveryOption(Quote q)
    {
        if (!q.RyvePoolDeliverySelected)
            return null;

        return new QuoteDeliveryOptionResponse(
            true,
            q.RyvePoolDeliveryStatus ?? "quoted",
            q.RyvePoolDeliveryDispatchTiming ?? RyvePoolDispatchCoordinator.DispatchTimingImmediate,
            q.RyvePoolDeliveryScheduledForUtc,
            q.RyvePoolDeliveryCurrency ?? q.Currency,
            q.RyvePoolDeliveryFeeMinor,
            MinorToMajor(q.RyvePoolDeliveryFeeMinor),
            q.RyvePoolPackageType,
            q.RyvePoolRegionCode,
            q.RyvePoolDispatchMode,
            new QuoteDeliveryPoint(null, q.RyvePoolPickupName, q.RyvePoolPickupAddress, q.RyvePoolPickupLat, q.RyvePoolPickupLng),
            new QuoteDeliveryPoint(q.RyvePoolDhlPointId, q.RyvePoolDhlPointName ?? q.RyvePoolDropoffName, q.RyvePoolDropoffAddress, q.RyvePoolDropoffLat, q.RyvePoolDropoffLng));
    }

    private static DeliveryPrice ExtractDeliveryPrice(JsonElement root, string defaultCurrency)
    {
        var currency = ReadString(root, "currency")
            ?? ReadString(root, "data", "currency")
            ?? defaultCurrency;

        var totalMinor = ReadLong(root, "breakdown", "totalMinor")
            ?? ReadLong(root, "breakdown", "total_minor")
            ?? ReadLong(root, "totalMinor")
            ?? ReadLong(root, "total_minor")
            ?? ReadLong(root, "deliveryFeeMinor")
            ?? ReadLong(root, "delivery_fee_minor")
            ?? ReadLong(root, "data", "breakdown", "totalMinor")
            ?? ReadLong(root, "data", "breakdown", "total_minor")
            ?? ReadLong(root, "data", "totalMinor")
            ?? ReadLong(root, "data", "total_minor");

        if (!totalMinor.HasValue || totalMinor.Value < 0)
        {
            throw new RyvePoolException(
                "ryvepool_quote_unpriced",
                "RyvePool quote did not include a payable delivery total.",
                StatusCodes.Status502BadGateway);
        }

        return new DeliveryPrice(totalMinor.Value, currency.ToUpperInvariant());
    }

    private static string? ReadString(JsonElement root, params string[] path)
    {
        if (!TryGetPath(root, path, out var value))
            return null;

        return value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    private static long? ReadLong(JsonElement root, params string[] path)
    {
        if (!TryGetPath(root, path, out var value))
            return null;

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number))
            return number;

        if (value.ValueKind == JsonValueKind.String && long.TryParse(value.GetString(), out var parsed))
            return parsed;

        return null;
    }

    private static bool TryGetPath(JsonElement root, string[] path, out JsonElement value)
    {
        value = root;
        foreach (var segment in path)
        {
            if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(segment, out value))
                return false;
        }

        return true;
    }

    private static IResult RyvePoolError(RyvePoolException ex)
    {
        var statusCode = ex.HttpStatusCode == 0
            ? StatusCodes.Status503ServiceUnavailable
            : ex.HttpStatusCode;
        return Results.Json(new ApiError(ex.ErrorCode, ex.Message), statusCode: statusCode);
    }

    private static decimal GetShippingSubtotal(Quote quote) =>
        Math.Round(quote.DhlBaseRate * (1 + quote.MarkupPercent / 100m), 2);

    private static decimal MinorToMajor(long minor) =>
        Math.Round(minor / 100m, 2);

    private static Guid? TryGetUserId(HttpContext ctx)
    {
        var sub = ctx.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? ctx.User.FindFirstValue("sub");
        return sub is null ? null : Guid.TryParse(sub, out var id) ? id : null;
    }

    private static Guid GetUserId(HttpContext ctx)
    {
        var sub = ctx.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? ctx.User.FindFirstValue("sub")
            ?? throw new UnauthorizedAccessException();
        return Guid.Parse(sub);
    }

    private static string DefaultCity(string countryCode) => countryCode.ToUpperInvariant() switch
    {
        "CA" => "Toronto",
        "US" => "New York",
        "GH" => "Accra",
        "NG" => "Lagos",
        "KE" => "Nairobi",
        "ZA" => "Johannesburg",
        "ET" => "Addis Ababa",
        "GB" => "London",
        "DE" => "Berlin",
        "AU" => "Sydney",
        "NL" => "Amsterdam",
        "CN" => "Shanghai",
        "HK" => "Hong Kong",
        "AE" => "Dubai",
        "PA" => "Panama City",
        _ => "Unknown"
    };

    private static string NormalizeIncoterm(string? incoterm) =>
        incoterm?.Trim().ToUpperInvariant() switch
        {
            "DDP" => "DDP",
            _ => "DAP"
        };

    private static bool IsValidCurrency(string currency) =>
        currency.Length == 3 && currency.All(char.IsLetter);

    private static bool RequiresPostalCodeForDhlRate(string countryCode) =>
        countryCode.ToUpperInvariant() switch
        {
            "CA" or "US" or "GH" or "NG" => true,
            _ => false
        };

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private record DeliveryPrice(long TotalMinor, string Currency);
}
