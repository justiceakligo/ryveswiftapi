using System.Text.Json.Serialization;

namespace RyveSwift.Api.Dhl;

// ─── Request ───────────────────────────────────────────────────────────────

public class DhlRatesRequest
{
    [JsonPropertyName("customerDetails")]
    public DhlRateCustomerDetails CustomerDetails { get; set; } = new();

    [JsonPropertyName("accounts")]
    public List<DhlAccount> Accounts { get; set; } = new();

    [JsonPropertyName("productCode")]
    public string ProductCode { get; set; } = "P";

    [JsonPropertyName("localProductCode")]
    public string LocalProductCode { get; set; } = "P";

    [JsonPropertyName("valueAddedServices")]
    public List<DhlValueAddedService>? ValueAddedServices { get; set; }

    [JsonPropertyName("monetaryAmount")]
    public List<DhlMonetaryAmount>? MonetaryAmount { get; set; }

    [JsonPropertyName("packages")]
    public List<DhlRatePackage> Packages { get; set; } = new();

    [JsonPropertyName("unitOfMeasurement")]
    public string UnitOfMeasurement { get; set; } = "metric";

    [JsonPropertyName("isCustomsDeclarable")]
    public bool IsCustomsDeclarable { get; set; } = true;

    [JsonPropertyName("nextBusinessDay")]
    public bool NextBusinessDay { get; set; } = false;

    [JsonPropertyName("requestAllValueAddedServices")]
    public bool RequestAllValueAddedServices { get; set; } = false;

    [JsonPropertyName("returnStandardProductsOnly")]
    public bool ReturnStandardProductsOnly { get; set; } = true;

    [JsonPropertyName("plannedShippingDateAndTime")]
    public string? PlannedShippingDateAndTime { get; set; }
}

public class DhlRateCustomerDetails
{
    [JsonPropertyName("shipperDetails")]
    public DhlRateAddressDetails ShipperDetails { get; set; } = new();

    [JsonPropertyName("receiverDetails")]
    public DhlRateAddressDetails ReceiverDetails { get; set; } = new();
}

public class DhlRateAddressDetails
{
    [JsonPropertyName("postalCode")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PostalCode { get; set; }

    [JsonPropertyName("cityName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CityName { get; set; }

    [JsonPropertyName("countryCode")]
    public string CountryCode { get; set; } = "";
}

public class DhlAccount
{
    [JsonPropertyName("typeCode")]
    public string TypeCode { get; set; } = "";

    [JsonPropertyName("number")]
    public string Number { get; set; } = "";
}

public class DhlValueAddedService
{
    [JsonPropertyName("serviceCode")]
    public string ServiceCode { get; set; } = "";
}

public class DhlMonetaryAmount
{
    [JsonPropertyName("typeCode")]
    public string TypeCode { get; set; } = "declaredValue";

    [JsonPropertyName("value")]
    public decimal Value { get; set; }

    [JsonPropertyName("currency")]
    public string Currency { get; set; } = "CAD";
}

public class DhlRatePackage
{
    [JsonPropertyName("weight")]
    public decimal Weight { get; set; }

    [JsonPropertyName("dimensions")]
    public DhlDimensions Dimensions { get; set; } = new();
}

public class DhlDimensions
{
    [JsonPropertyName("length")]
    public decimal Length { get; set; }

    [JsonPropertyName("width")]
    public decimal Width { get; set; }

    [JsonPropertyName("height")]
    public decimal Height { get; set; }
}

// ─── Response ──────────────────────────────────────────────────────────────

public class DhlRatesResponse
{
    [JsonPropertyName("products")]
    public List<DhlProduct> Products { get; set; } = new();
}

public class DhlProduct
{
    [JsonPropertyName("productCode")]
    public string ProductCode { get; set; } = "";

    [JsonPropertyName("localProductCode")]
    public string LocalProductCode { get; set; } = "";

    [JsonPropertyName("productName")]
    public string ProductName { get; set; } = "";

    [JsonPropertyName("totalPrice")]
    public List<DhlTotalPrice> TotalPrice { get; set; } = new();

    [JsonPropertyName("deliveryCapabilities")]
    public DhlDeliveryCapabilities? DeliveryCapabilities { get; set; }
}

public class DhlTotalPrice
{
    [JsonPropertyName("currencyType")]
    public string CurrencyType { get; set; } = "";

    [JsonPropertyName("price")]
    public decimal Price { get; set; }

    [JsonPropertyName("priceCurrency")]
    public string PriceCurrency { get; set; } = "";
}

public class DhlDeliveryCapabilities
{
    [JsonPropertyName("estimatedDeliveryDateAndTime")]
    public string? EstimatedDeliveryDateAndTime { get; set; }
}
