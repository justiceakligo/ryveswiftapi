using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using RyveSwift.Api.Common;
using RyveSwift.Api.Dhl;
using RyveSwift.Api.Entities;

namespace RyveSwift.Api.Services;

public class DhlService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ConfigService _config;
    private readonly ILogger<DhlService> _logger;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = null,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public DhlService(IHttpClientFactory httpClientFactory, ConfigService config, ILogger<DhlService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _config = config;
        _logger = logger;
    }

    // ─── Rate Quote ────────────────────────────────────────────────────────

    public async Task<(decimal baseRate, string currency, string rawJson)> GetRateAsync(
        string originCountry, string originCity, string? originPostalCode,
        string destCountry, string destCity, string? destPostalCode,
        decimal weightKg, decimal lengthCm, decimal widthCm, decimal heightCm,
        string productCode,
        decimal? declaredValue = null,
        string? declaredValueCurrency = null)
    {
        originCountry = NormalizeCountryCode(originCountry);
        destCountry = NormalizeCountryCode(destCountry);
        productCode = productCode.ToUpperInvariant();

        if (DhlProductPolicy.TryGetHiddenServiceName(productCode, null, out var hiddenService))
            throw new DhlException("UNSUPPORTED_DHL_SERVICE", DhlProductPolicy.HiddenServiceMessage(hiddenService), 422);

        var shippingDateStr = GetNextBusinessDay(DateTime.UtcNow).ToString("yyyy-MM-dd") + "T14:00:00 GMT+00:00";
        var unitOfMeasurement = UnitOfMeasurementFor(originCountry);
        var isCustomsDeclarable = IsCustomsDeclarable(productCode, originCountry, destCountry);

        var request = new DhlRatesRequest
        {
            ProductCode = productCode,
            LocalProductCode = productCode,
            UnitOfMeasurement = unitOfMeasurement,
            IsCustomsDeclarable = isCustomsDeclarable,
            PlannedShippingDateAndTime = shippingDateStr,
            CustomerDetails = new DhlRateCustomerDetails
            {
                ShipperDetails = new DhlRateAddressDetails
                {
                    CountryCode = originCountry,
                    CityName = string.IsNullOrWhiteSpace(originCity) ? DefaultCityFor(originCountry) : originCity,
                    PostalCode = NormalizePostalCode(originCountry, originPostalCode)
                },
                ReceiverDetails = new DhlRateAddressDetails
                {
                    CountryCode = destCountry,
                    CityName = string.IsNullOrWhiteSpace(destCity) ? DefaultCityFor(destCountry) : destCity,
                    PostalCode = NormalizePostalCode(destCountry, destPostalCode)
                }
            },
            Accounts = GetRateAccounts(originCountry),
            MonetaryAmount = isCustomsDeclarable
                ? new List<DhlMonetaryAmount>
                {
                    new()
                    {
                        TypeCode = "declaredValue",
                        Value = declaredValue.GetValueOrDefault(100m),
                        Currency = NormalizeCurrency(declaredValueCurrency)
                    }
                }
                : null,
            ValueAddedServices = isCustomsDeclarable ? new List<DhlValueAddedService> { new() { ServiceCode = "WY" } } : null,
            Packages = new List<DhlRatePackage>
            {
                new()
                {
                    Weight = ConvertWeight(weightKg, unitOfMeasurement),
                    Dimensions = ConvertDimensions(lengthCm, widthCm, heightCm, unitOfMeasurement)
                }
            }
        };

        var client = CreateAuthenticatedClient();
        var json = JsonSerializer.Serialize(request, JsonOpts);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        _logger.LogInformation("Calling DHL /rates for {Origin}→{Dest}", originCountry, destCountry);

        var response = await client.PostAsync("rates", content);
        var responseBody = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("DHL /rates returned {Status}: {Body}", response.StatusCode, responseBody);
            throw BuildDhlException("DHL_RATE_FAILED", "rate request", response.StatusCode, responseBody);
        }

        var rateResponse = JsonSerializer.Deserialize<DhlRatesResponse>(responseBody, JsonOpts)
            ?? throw new DhlException("DHL_RATE_FAILED", "Empty response from DHL rates endpoint.");

        var allowedProducts = rateResponse.Products
            .Where(p => !DhlProductPolicy.IsHiddenService(p))
            .ToList();

        var product = allowedProducts.FirstOrDefault(p =>
            p.ProductCode.Equals(productCode, StringComparison.OrdinalIgnoreCase))
            ?? throw new DhlException("DHL_RATE_FAILED", $"No rate returned for product code {productCode}.");

        // Prefer billing currency price (BILLC)
        var priceEntry = product.TotalPrice.FirstOrDefault(p => p.CurrencyType == "BILLC")
            ?? product.TotalPrice.FirstOrDefault()
            ?? throw new DhlException("DHL_RATE_FAILED", "No price found in DHL rate response.");

        return (priceEntry.Price, priceEntry.PriceCurrency, responseBody);
    }

    // ─── Create Shipment ───────────────────────────────────────────────────

    public async Task<DhlShipmentResponse> CreateShipmentAsync(
        Shipment shipment, Address sender, Address receiver,
        List<ShipmentPackage> packages, List<CustomsItem> customsItems)
    {
        var originCountry = NormalizeCountryCode(shipment.OriginCountry);
        var destCountry = NormalizeCountryCode(shipment.DestinationCountry);
        var productCode = shipment.ProductCode.ToUpperInvariant();
        var incoterm = NormalizeIncoterm(shipment.Incoterm);
        var unitOfMeasurement = UnitOfMeasurementFor(originCountry);
        var isCustomsDeclarable = IsCustomsDeclarable(productCode, originCountry, destCountry);

        if (DhlProductPolicy.TryGetHiddenServiceName(productCode, null, out var hiddenService))
            throw new DhlException("UNSUPPORTED_DHL_SERVICE", DhlProductPolicy.HiddenServiceMessage(hiddenService), 422);

        if (isCustomsDeclarable)
        {
            if (customsItems.Count == 0)
                throw new DhlException("INVALID_CUSTOMS_DATA", "Customs items are required for parcel shipments.", 422);

            var customsErrors = CustomsValidation.ValidateCustomsItems(customsItems, requireHsCode: true);
            if (customsErrors.Count > 0)
                throw new DhlException("INVALID_CUSTOMS_DATA",
                    "One or more customs items are missing required clearance details.", 422);
        }

        // Build shipping date (next business day, 14:00 UTC)
        var shippingDate = GetNextBusinessDay(DateTime.UtcNow);
        var dateStr = shippingDate.ToString("yyyy-MM-ddTHH:mm:ss") + " GMT+00:00";

        // Build customs line items
        var lineItems = customsItems.Select((item, idx) => new DhlLineItem
        {
            Number = idx + 1,
            Description = CustomsValidation.NormalizeDescription(item.Description),
            Price = item.UnitPrice,
            PriceCurrency = NormalizeCurrency(item.Currency),
            Quantity = new DhlLineItemQuantity { Value = item.Quantity, UnitOfMeasurement = item.UnitOfMeasurement },
            CommodityCodes = new List<DhlCommodityCode> { new() { TypeCode = "outbound", Value = CustomsValidation.NormalizeHsCode(item.HsCode) } },
            ManufacturerCountry = NormalizeCountryCode(item.ManufacturerCountry),
            Weight = new DhlItemWeight
            {
                NetValue = ConvertWeight(item.NetWeightKg, unitOfMeasurement),
                GrossValue = ConvertWeight(item.GrossWeightKg, unitOfMeasurement)
            }
        }).ToList();

        var declaredValue = customsItems.Sum(i => i.UnitPrice * i.Quantity);
        var contentDescription = lineItems.FirstOrDefault()?.Description ?? productCode;

        var request = new DhlShipmentRequest
        {
            PlannedShippingDateAndTime = dateStr,
            Pickup = new DhlPickup { IsRequested = false },
            ProductCode = productCode,
            LocalProductCode = productCode,
            Accounts = GetShipmentAccounts(originCountry, destCountry, incoterm),
            ValueAddedServices = isCustomsDeclarable
                ? new List<DhlValueAddedService> { new() { ServiceCode = "WY" } }
                : null,
            OutputImageProperties = DhlOutputImageProperties.ForShipment(isCustomsDeclarable),
            CustomerDetails = new DhlShipmentCustomerDetails
            {
                ShipperDetails = BuildPartyDetails(sender),
                ReceiverDetails = BuildPartyDetails(receiver)
            },
            Content = new DhlContent
            {
                Packages = packages.Select(p => new DhlContentPackage
                {
                    Weight = ConvertWeight(p.WeightKg, unitOfMeasurement),
                    Dimensions = ConvertDimensions(p.LengthCm, p.WidthCm, p.HeightCm, unitOfMeasurement)
                }).ToList(),
                IsCustomsDeclarable = isCustomsDeclarable,
                DeclaredValue = isCustomsDeclarable ? declaredValue : null,
                DeclaredValueCurrency = isCustomsDeclarable ? NormalizeCurrency(customsItems.FirstOrDefault()?.Currency) : null,
                UnitOfMeasurement = unitOfMeasurement,
                Description = contentDescription,
                Incoterm = incoterm,
                ExportDeclaration = isCustomsDeclarable
                    ? new DhlExportDeclaration
                    {
                        LineItems = lineItems,
                        Invoice = new DhlInvoice
                        {
                            Number = shipment.InvoiceNumber ?? $"INV-{shipment.Id.ToString("N")[..31]}",
                            Date = (shipment.InvoiceDate ?? DateTime.UtcNow).ToString("yyyy-MM-dd")
                        },
                        ExportReason = NormalizeExportReason(shipment.ExportReason),
                        ExportReasonType = "permanent"
                    }
                    : null
            }
        };

        var client = CreateAuthenticatedClient();
        var json = JsonSerializer.Serialize(request, JsonOpts);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        _logger.LogInformation("Calling DHL /shipments for shipment {ShipmentId}", shipment.Id);

        var response = await client.PostAsync("shipments", content);
        var responseBody = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("DHL /shipments returned {Status}: {Body}", response.StatusCode, responseBody);
            throw BuildDhlException("DHL_SHIPMENT_FAILED", "shipment creation", response.StatusCode, responseBody);
        }

        var shipmentResponse = JsonSerializer.Deserialize<DhlShipmentResponse>(responseBody, JsonOpts)
            ?? throw new DhlException("DHL_SHIPMENT_FAILED", "Empty response from DHL shipments endpoint.");

        return shipmentResponse;
    }

    // ─── Tracking ──────────────────────────────────────────────────────────

    public async Task<DhlTrackingResponse> GetTrackingAsync(string trackingNumber)
    {
        var client = CreateAuthenticatedClient();
        var url = $"tracking?shipmentTrackingNumber={Uri.EscapeDataString(trackingNumber)}&trackingView=all-checkpoints";

        _logger.LogInformation("Calling DHL /tracking for {TrackingNumber}", trackingNumber);

        var response = await client.GetAsync(url);
        var responseBody = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("DHL /tracking returned {Status}: {Body}", response.StatusCode, responseBody);
            throw BuildDhlException("DHL_TRACKING_FAILED", "tracking request", response.StatusCode, responseBody);
        }

        return JsonSerializer.Deserialize<DhlTrackingResponse>(responseBody, JsonOpts)
            ?? new DhlTrackingResponse();
    }

    // ─── Helpers ───────────────────────────────────────────────────────────

    private HttpClient CreateAuthenticatedClient()
    {
        var apiKey = _config.Get("DHL_API_KEY");
        var apiSecret = _config.Get("DHL_API_SECRET");
        var baseUrl = _config.Get("DHL_BASE_URL");
        var timeoutSeconds = _config.GetInt("DHL_TIMEOUT_SECONDS", 30);

        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{apiKey}:{apiSecret}"));

        var client = _httpClientFactory.CreateClient("dhl");
        client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
        client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        client.DefaultRequestHeaders.TryAddWithoutValidation("Message-Reference", Guid.NewGuid().ToString());
        return client;
    }

    private List<DhlAccount> GetShipmentAccounts(string originCountry, string destCountry, string incoterm)
    {
        var exportAccount = _config.Get("DHL_ACCOUNT_NUMBER");
        var importAccount = GetImportAccount();
        var originIsCanada = originCountry.Equals("CA", StringComparison.OrdinalIgnoreCase);
        var isInternational = !originCountry.Equals(destCountry, StringComparison.OrdinalIgnoreCase);
        var shipperAccount = originIsCanada ? exportAccount : importAccount;
        var payerAccount = isInternational ? importAccount : exportAccount;

        var accounts = new List<DhlAccount>
        {
            new() { TypeCode = "shipper", Number = shipperAccount },
            new() { TypeCode = "payer", Number = payerAccount }
        };

        if (incoterm.Equals("DDP", StringComparison.OrdinalIgnoreCase))
        {
            accounts.Add(new DhlAccount
            {
                TypeCode = "duties-taxes",
                Number = originIsCanada ? exportAccount : importAccount
            });
        }

        return accounts;
    }

    private List<DhlAccount> GetRateAccounts(string originCountry)
    {
        var exportAccount = _config.Get("DHL_ACCOUNT_NUMBER");
        var importAccount = GetImportAccount();

        return new List<DhlAccount>
        {
            new()
            {
                TypeCode = "shipper",
                Number = originCountry.Equals("CA", StringComparison.OrdinalIgnoreCase) ? exportAccount : importAccount
            }
        };
    }

    private static DhlPartyDetails BuildPartyDetails(Address address)
    {
        var countryCode = NormalizeCountryCode(address.CountryCode);

        return new DhlPartyDetails
        {
            PostalAddress = new DhlPostalAddress
            {
                CountryCode = countryCode,
                CityName = string.IsNullOrWhiteSpace(address.CityName)
                    ? DefaultCityFor(countryCode)
                    : address.CityName.Trim(),
                PostalCode = NormalizePostalCode(countryCode, address.PostalCode),
                AddressLine1 = string.IsNullOrWhiteSpace(address.AddressLine1) ? "N/A" : address.AddressLine1.Trim(),
                AddressLine2 = CleanOptional(address.AddressLine2),
                CountyName = CleanOptional(address.AddressLine3, maxLength: 45)
            },
            ContactInformation = new DhlContactInformation
            {
                FullName = string.IsNullOrWhiteSpace(address.ContactName) ? "Contact" : address.ContactName.Trim(),
                CompanyName = string.IsNullOrWhiteSpace(address.CompanyName)
                    ? (string.IsNullOrWhiteSpace(address.ContactName) ? "Contact" : address.ContactName.Trim())
                    : address.CompanyName.Trim(),
                Phone = NormalizePhone(address.Phone),
                Email = CleanOptional(address.Email)
            }
        };
    }

    private static DateTime GetNextBusinessDay(DateTime from)
    {
        var next = from.Date.AddDays(1);
        while (next.DayOfWeek == DayOfWeek.Saturday || next.DayOfWeek == DayOfWeek.Sunday)
            next = next.AddDays(1);
        return next.AddHours(14); // 14:00 UTC
    }

    private static string DefaultCityFor(string countryCode) => countryCode.ToUpperInvariant() switch
    {
        "CA" => "Toronto",
        "US" => "New York",
        "GH" => "Accra",
        "NG" => "Lagos",
        "GB" => "London",
        "DE" => "Berlin",
        "AU" => "Sydney",
        "NL" => "Amsterdam",
        "CN" => "Shanghai",
        "HK" => "Hong Kong",
        "AE" => "Dubai",
        "PA" => "Panama City",
        _    => "Unknown"
    };

    private static string? DefaultPostalCodeFor(string countryCode) => countryCode.ToUpperInvariant() switch
    {
        "AG" => "00265",
        _    => null
    };

    private string GetImportAccount()
    {
        var importAccount = _config.Get("DHL_IMPORT_ACCOUNT_NUMBER", "");
        if (!IsPlaceholder(importAccount))
            return importAccount;

        var legacyImportAccount = _config.Get("DHL_IMPORT_ACCOUNT", "");
        if (!IsPlaceholder(legacyImportAccount))
            return legacyImportAccount;

        return string.IsNullOrWhiteSpace(importAccount) ? legacyImportAccount : importAccount;
    }

    private static DhlException BuildDhlException(
        string errorCode,
        string operation,
        HttpStatusCode statusCode,
        string responseBody)
    {
        var message = FriendlyDhlMessage(operation, statusCode, responseBody);
        return new DhlException(errorCode, message, (int)statusCode, responseBody);
    }

    private static string FriendlyDhlMessage(string operation, HttpStatusCode statusCode, string responseBody)
    {
        if (responseBody.Contains("IMP enabled", StringComparison.OrdinalIgnoreCase) ||
            responseBody.Contains("8009", StringComparison.OrdinalIgnoreCase))
            return "DHL rejected the account for this route. Use the import/IMPEX account for non-Canada-origin shipments.";

        if (responseBody.Contains("420506", StringComparison.OrdinalIgnoreCase))
            return "DHL rejected the US ZIP code. Use a valid 5- or 9-digit US ZIP code.";

        if (responseBody.Contains("cityDistrict", StringComparison.OrdinalIgnoreCase) ||
            responseBody.Contains("extraneous key not permitted", StringComparison.OrdinalIgnoreCase))
            return "DHL rejected an unsupported address district field. Put suburbs or districts in countyName.";

        if (responseBody.Contains("postalCode", StringComparison.OrdinalIgnoreCase) &&
            responseBody.Contains("required", StringComparison.OrdinalIgnoreCase))
            return "DHL requires a valid postalCode for this address. Use the real postal code, or a DHL-recognized service-area code where postal codes are not used.";

        var detail = TryReadDhlDetail(responseBody);
        return detail is null
            ? $"DHL {operation} failed: {(int)statusCode} {statusCode}."
            : $"DHL {operation} failed: {detail}";
    }

    private static string? TryReadDhlDetail(string responseBody)
    {
        try
        {
            using var document = JsonDocument.Parse(responseBody);
            if (document.RootElement.TryGetProperty("detail", out var detail) &&
                detail.ValueKind == JsonValueKind.String)
                return detail.GetString();

            if (document.RootElement.TryGetProperty("message", out var message) &&
                message.ValueKind == JsonValueKind.String)
                return message.GetString();
        }
        catch (JsonException)
        {
        }

        return string.IsNullOrWhiteSpace(responseBody) ? null : responseBody;
    }

    private static bool IsCustomsDeclarable(string productCode, string originCountry, string destCountry) =>
        !originCountry.Equals(destCountry, StringComparison.OrdinalIgnoreCase) &&
        !productCode.Equals("D", StringComparison.OrdinalIgnoreCase);

    private static string UnitOfMeasurementFor(string originCountry) =>
        originCountry.Equals("US", StringComparison.OrdinalIgnoreCase) ? "imperial" : "metric";

    private static decimal ConvertWeight(decimal weightKg, string unitOfMeasurement) =>
        unitOfMeasurement == "imperial" ? RoundDhlDecimal(weightKg * 2.20462262185m) : RoundDhlDecimal(weightKg);

    private static DhlDimensions ConvertDimensions(decimal lengthCm, decimal widthCm, decimal heightCm, string unitOfMeasurement) =>
        unitOfMeasurement == "imperial"
            ? new DhlDimensions
            {
                Length = RoundDhlDecimal(lengthCm / 2.54m),
                Width = RoundDhlDecimal(widthCm / 2.54m),
                Height = RoundDhlDecimal(heightCm / 2.54m)
            }
            : new DhlDimensions
            {
                Length = RoundDhlDecimal(lengthCm),
                Width = RoundDhlDecimal(widthCm),
                Height = RoundDhlDecimal(heightCm)
            };

    private static decimal RoundDhlDecimal(decimal value) =>
        Math.Round(value, 3, MidpointRounding.AwayFromZero);

    private static string NormalizeCountryCode(string countryCode) =>
        string.IsNullOrWhiteSpace(countryCode) ? "" : countryCode.Trim().ToUpperInvariant();

    private static string NormalizeCurrency(string? currency) =>
        string.IsNullOrWhiteSpace(currency) ? "CAD" : currency.Trim().ToUpperInvariant();

    private static string NormalizeIncoterm(string? incoterm) =>
        incoterm?.Trim().ToUpperInvariant() == "DDP" ? "DDP" : "DAP";

    private static string NormalizeExportReason(string? exportReason)
    {
        var normalized = exportReason?.Trim().ToLowerInvariant();
        return normalized switch
        {
            null or "" => "sale",
            "sold" or "sale" or "sales" => "sale",
            "personal_use" or "personal use" => "personal",
            _ => normalized
        };
    }

    private static string NormalizePhone(string? phone)
    {
        var digits = new string((phone ?? "").Where(char.IsDigit).ToArray());
        return digits.Length >= 7 ? digits : "0000000000";
    }

    private static string? NormalizePostalCode(string countryCode, string? postalCode)
    {
        var normalizedCountry = NormalizeCountryCode(countryCode);
        var value = postalCode?.Trim();
        if (string.IsNullOrWhiteSpace(value))
            value = DefaultPostalCodeFor(normalizedCountry);

        if (string.IsNullOrWhiteSpace(value))
            return null;

        var digitsOnly = new string(value.Where(char.IsDigit).ToArray());
        if (!string.IsNullOrEmpty(digitsOnly) && digitsOnly.All(c => c == '0'))
            return null;

        if (normalizedCountry == "US")
        {
            if (string.IsNullOrEmpty(digitsOnly))
                return null;
            if (digitsOnly.Length < 5)
                return digitsOnly.PadLeft(5, '0');
            if (digitsOnly.Length == 5 || digitsOnly.Length == 9)
                return digitsOnly;
            return digitsOnly.Length > 9 ? digitsOnly[..9] : digitsOnly[..5];
        }

        if (normalizedCountry == "CA")
            return value.Replace(" ", "").ToUpperInvariant();

        return value.ToUpperInvariant();
    }

    private static string? CleanOptional(string? value, int? maxLength = null)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var cleaned = value.Trim();
        return maxLength.HasValue && cleaned.Length > maxLength.Value
            ? cleaned[..maxLength.Value]
            : cleaned;
    }

    private static bool IsPlaceholder(string? value) =>
        string.IsNullOrWhiteSpace(value) ||
        value.StartsWith("PLACEHOLDER_", StringComparison.OrdinalIgnoreCase);
}

public class DhlException : Exception
{
    public string ErrorCode { get; }
    public int HttpStatusCode { get; }
    public string? RawResponse { get; }
    public bool IsClientError => HttpStatusCode is >= 400 and < 500;
    public DhlException(string errorCode, string message, int httpStatusCode = 0, string? rawResponse = null) : base(message)
    {
        ErrorCode = errorCode;
        HttpStatusCode = httpStatusCode;
        RawResponse = rawResponse;
    }
}
