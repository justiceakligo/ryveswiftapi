using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
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
        string productCode)
    {
        // CA→CA domestic is not supported
        if (originCountry.Equals("CA", StringComparison.OrdinalIgnoreCase) &&
            destCountry.Equals("CA", StringComparison.OrdinalIgnoreCase))
        {
            throw new DhlException("UNSUPPORTED_ROUTE", "Canada domestic shipments are not supported by this account.");
        }

        var shippingDateStr = GetNextBusinessDay(DateTime.UtcNow).ToString("yyyy-MM-dd") + "T14:00:00 GMT+00:00";

        var request = new DhlRatesRequest
        {
            ProductCode = productCode,
            LocalProductCode = productCode,
            IsCustomsDeclarable = !productCode.Equals("D", StringComparison.OrdinalIgnoreCase),
            PlannedShippingDateAndTime = shippingDateStr,
            CustomerDetails = new DhlRateCustomerDetails
            {
                ShipperDetails = new DhlRateAddressDetails
                {
                    CountryCode = originCountry.ToUpper(),
                    CityName = string.IsNullOrWhiteSpace(originCity) ? DefaultCityFor(originCountry) : originCity,
                    PostalCode = string.IsNullOrWhiteSpace(originPostalCode) ? DefaultPostalCodeFor(originCountry) : originPostalCode
                },
                ReceiverDetails = new DhlRateAddressDetails
                {
                    CountryCode = destCountry.ToUpper(),
                    CityName = string.IsNullOrWhiteSpace(destCity) ? DefaultCityFor(destCountry) : destCity,
                    PostalCode = string.IsNullOrWhiteSpace(destPostalCode) ? DefaultPostalCodeFor(destCountry) : destPostalCode
                }
            },
            Accounts = GetRateAccounts(originCountry),
            ValueAddedServices = productCode != "D" ? new List<DhlValueAddedService> { new() { ServiceCode = "WY" } } : null,
            Packages = new List<DhlRatePackage>
            {
                new()
                {
                    Weight = weightKg,
                    Dimensions = new DhlDimensions { Length = lengthCm, Width = widthCm, Height = heightCm }
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
            throw new DhlException("DHL_RATE_FAILED", $"DHL rate request failed: {response.StatusCode}");
        }

        var rateResponse = JsonSerializer.Deserialize<DhlRatesResponse>(responseBody, JsonOpts)
            ?? throw new DhlException("DHL_RATE_FAILED", "Empty response from DHL rates endpoint.");

        // Find the matching product by code
        var product = rateResponse.Products.FirstOrDefault(p =>
            p.ProductCode.Equals(productCode, StringComparison.OrdinalIgnoreCase))
            ?? rateResponse.Products.FirstOrDefault()
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
        var isDocumentsOnly = shipment.ProductCode.Equals("D", StringComparison.OrdinalIgnoreCase);

        // Build shipping date (next business day, 14:00 UTC)
        var shippingDate = GetNextBusinessDay(DateTime.UtcNow);
        var dateStr = shippingDate.ToString("yyyy-MM-ddTHH:mm:ss") + " GMT+00:00";

        // Build customs line items
        var lineItems = customsItems.Select((item, idx) => new DhlLineItem
        {
            Number = idx + 1,
            Description = item.Description,
            Price = item.UnitPrice,
            PriceCurrency = item.Currency,
            Quantity = new DhlLineItemQuantity { Value = item.Quantity, UnitOfMeasurement = item.UnitOfMeasurement },
            CommodityCodes = new List<DhlCommodityCode> { new() { TypeCode = "outbound", Value = item.HsCode } },
            ExportReasonType = "permanent",
            ManufacturerCountry = item.ManufacturerCountry.ToUpper(),
            Weight = new DhlItemWeight { NetValue = item.NetWeightKg, GrossValue = item.GrossWeightKg }
        }).ToList();

        var declaredValue = customsItems.Sum(i => i.UnitPrice * i.Quantity);

        var request = new DhlShipmentRequest
        {
            PlannedShippingDateAndTime = dateStr,
            Pickup = new DhlPickup { IsRequested = false },
            ProductCode = shipment.ProductCode,
            LocalProductCode = shipment.ProductCode,
            Accounts = GetShipmentAccounts(shipment.OriginCountry),
            ValueAddedServices = !isDocumentsOnly
                ? new List<DhlValueAddedService> { new() { ServiceCode = "WY" } }
                : null,
            OutputImageProperties = new DhlOutputImageProperties(),
            CustomerDetails = new DhlShipmentCustomerDetails
            {
                ShipperDetails = BuildPartyDetails(sender),
                ReceiverDetails = BuildPartyDetails(receiver)
            },
            Content = new DhlContent
            {
                Packages = packages.Select(p => new DhlContentPackage
                {
                    Weight = p.WeightKg,
                    Dimensions = new DhlDimensions { Length = p.LengthCm, Width = p.WidthCm, Height = p.HeightCm }
                }).ToList(),
                IsCustomsDeclarable = !isDocumentsOnly,
                DeclaredValue = declaredValue,
                DeclaredValueCurrency = customsItems.FirstOrDefault()?.Currency ?? "CAD",
                Description = customsItems.Count > 0
                    ? string.Join(", ", customsItems.Select(i => i.Description).Distinct().Take(3))
                    : shipment.ProductCode,
                Incoterm = "DAP",
                ExportDeclaration = !isDocumentsOnly
                    ? new DhlExportDeclaration
                    {
                        LineItems = lineItems,
                        Invoice = new DhlInvoice
                        {
                            Number = shipment.InvoiceNumber ?? $"INV-{shipment.Id:N}",
                            Date = (shipment.InvoiceDate ?? DateTime.UtcNow).ToString("yyyy-MM-dd")
                        },
                        ExportReason = shipment.ExportReason ?? "PERSONAL_USE"
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
            throw new DhlException("DHL_SHIPMENT_FAILED", $"DHL shipment creation failed: {response.StatusCode}", (int)response.StatusCode);
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
            throw new DhlException("DHL_TRACKING_FAILED", $"DHL tracking request failed: {response.StatusCode}");
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
        return client;
    }

    /// <summary>
    /// For CA-origin: use export (shipper) account for both shipper and payer.
    /// For non-CA origin: use import account as payer.
    /// </summary>
    private List<DhlAccount> GetShipmentAccounts(string originCountry)
    {
        var exportAccount = _config.Get("DHL_ACCOUNT_NUMBER");
        var importAccount = _config.Get("DHL_IMPORT_ACCOUNT");

        if (originCountry.Equals("CA", StringComparison.OrdinalIgnoreCase))
        {
            return new List<DhlAccount>
            {
                new() { TypeCode = "shipper", Number = exportAccount },
                new() { TypeCode = "payer", Number = exportAccount }
            };
        }
        else
        {
            // Never use the Canada export account as payer for non-CA-origin shipments
            return new List<DhlAccount>
            {
                new() { TypeCode = "payer", Number = importAccount }
            };
        }
    }

    private List<DhlAccount> GetRateAccounts(string originCountry)
    {
        var exportAccount = _config.Get("DHL_ACCOUNT_NUMBER");
        var importAccount = _config.Get("DHL_IMPORT_ACCOUNT");

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
        return new DhlPartyDetails
        {
            PostalAddress = new DhlPostalAddress
            {
                CountryCode = address.CountryCode.ToUpper(),
                CityName = address.CityName,
                PostalCode = string.IsNullOrWhiteSpace(address.PostalCode) ? null : address.PostalCode,
                AddressLine1 = address.AddressLine1,
                AddressLine2 = address.AddressLine2,
                AddressLine3 = address.AddressLine3
            },
            ContactInformation = new DhlContactInformation
            {
                FullName = address.ContactName,
                CompanyName = address.CompanyName,
                Phone = address.Phone,
                Email = address.Email
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
        _    => "Unknown"
    };

    private static string DefaultPostalCodeFor(string countryCode) => countryCode.ToUpperInvariant() switch
    {
        "CA" => "M5V 3A1",  // Toronto — valid Canadian format A9A 9A9
        "US" => "10001",    // New York
        "GH" => "00233",    // Ghana placeholder
        "NG" => "100001",   // Lagos placeholder
        _    => "00000"
    };
}

public class DhlException : Exception
{
    public string ErrorCode { get; }
    public int HttpStatusCode { get; }
    public bool IsClientError => HttpStatusCode is >= 400 and < 500;
    public DhlException(string errorCode, string message, int httpStatusCode = 0) : base(message)
    {
        ErrorCode = errorCode;
        HttpStatusCode = httpStatusCode;
    }
}
