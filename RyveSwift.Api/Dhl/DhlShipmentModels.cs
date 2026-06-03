using System.Text.Json.Serialization;

namespace RyveSwift.Api.Dhl;

// ─── Request ───────────────────────────────────────────────────────────────

public class DhlShipmentRequest
{
    [JsonPropertyName("plannedShippingDateAndTime")]
    public string PlannedShippingDateAndTime { get; set; } = "";

    [JsonPropertyName("pickup")]
    public DhlPickup Pickup { get; set; } = new();

    [JsonPropertyName("productCode")]
    public string ProductCode { get; set; } = "P";

    [JsonPropertyName("localProductCode")]
    public string LocalProductCode { get; set; } = "P";

    [JsonPropertyName("accounts")]
    public List<DhlAccount> Accounts { get; set; } = new();

    [JsonPropertyName("valueAddedServices")]
    public List<DhlValueAddedService>? ValueAddedServices { get; set; }

    [JsonPropertyName("outputImageProperties")]
    public DhlOutputImageProperties OutputImageProperties { get; set; } = new();

    [JsonPropertyName("customerDetails")]
    public DhlShipmentCustomerDetails CustomerDetails { get; set; } = new();

    [JsonPropertyName("content")]
    public DhlContent Content { get; set; } = new();
}

public class DhlPickup
{
    [JsonPropertyName("isRequested")]
    public bool IsRequested { get; set; } = false;
}

public class DhlOutputImageProperties
{
    [JsonPropertyName("printerDPI")]
    public int PrinterDpi { get; set; } = 300;

    [JsonPropertyName("encodingFormat")]
    public string EncodingFormat { get; set; } = "pdf";

    [JsonPropertyName("imageOptions")]
    public List<DhlImageOption> ImageOptions { get; set; } = new();

    public static DhlOutputImageProperties ForShipment(bool includeInvoice) => new()
    {
        ImageOptions = includeInvoice
            ? new List<DhlImageOption>
            {
                new() { TypeCode = "label", TemplateName = "ECOM26_84_001", IsRequested = true },
                new() { TypeCode = "invoice", TemplateName = "COMMERCIAL_INVOICE_P_10", IsRequested = true },
                new() { TypeCode = "waybillDoc", TemplateName = "ARCH_8X4", IsRequested = true }
            }
            : new List<DhlImageOption>
            {
                new() { TypeCode = "label", TemplateName = "ECOM26_84_001", IsRequested = true },
                new() { TypeCode = "waybillDoc", TemplateName = "ARCH_8X4", IsRequested = true }
            }
    };
}

public class DhlImageOption
{
    [JsonPropertyName("typeCode")]
    public string TypeCode { get; set; } = "";

    [JsonPropertyName("templateName")]
    public string TemplateName { get; set; } = "";

    [JsonPropertyName("isRequested")]
    public bool IsRequested { get; set; } = true;
}

public class DhlShipmentCustomerDetails
{
    [JsonPropertyName("shipperDetails")]
    public DhlPartyDetails ShipperDetails { get; set; } = new();

    [JsonPropertyName("receiverDetails")]
    public DhlPartyDetails ReceiverDetails { get; set; } = new();
}

public class DhlPartyDetails
{
    [JsonPropertyName("postalAddress")]
    public DhlPostalAddress PostalAddress { get; set; } = new();

    [JsonPropertyName("contactInformation")]
    public DhlContactInformation ContactInformation { get; set; } = new();
}

public class DhlPostalAddress
{
    [JsonPropertyName("postalCode")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PostalCode { get; set; }

    [JsonPropertyName("cityName")]
    public string CityName { get; set; } = "";

    [JsonPropertyName("countryCode")]
    public string CountryCode { get; set; } = "";

    [JsonPropertyName("addressLine1")]
    public string AddressLine1 { get; set; } = "";

    [JsonPropertyName("addressLine2")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AddressLine2 { get; set; }

    [JsonPropertyName("addressLine3")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AddressLine3 { get; set; }

    [JsonPropertyName("countyName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CountyName { get; set; }
}

public class DhlContactInformation
{
    [JsonPropertyName("fullName")]
    public string FullName { get; set; } = "";

    [JsonPropertyName("companyName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CompanyName { get; set; }

    [JsonPropertyName("phone")]
    public string Phone { get; set; } = "";

    [JsonPropertyName("email")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Email { get; set; }
}

public class DhlContent
{
    [JsonPropertyName("packages")]
    public List<DhlContentPackage> Packages { get; set; } = new();

    [JsonPropertyName("isCustomsDeclarable")]
    public bool IsCustomsDeclarable { get; set; } = true;

    [JsonPropertyName("declaredValue")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public decimal? DeclaredValue { get; set; }

    [JsonPropertyName("declaredValueCurrency")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DeclaredValueCurrency { get; set; }

    [JsonPropertyName("exportDeclaration")]
    public DhlExportDeclaration? ExportDeclaration { get; set; }

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("incoterm")]
    public string Incoterm { get; set; } = "DAP";

    [JsonPropertyName("unitOfMeasurement")]
    public string UnitOfMeasurement { get; set; } = "metric";
}

public class DhlContentPackage
{
    [JsonPropertyName("weight")]
    public decimal Weight { get; set; }

    [JsonPropertyName("dimensions")]
    public DhlDimensions Dimensions { get; set; } = new();
}

public class DhlExportDeclaration
{
    [JsonPropertyName("lineItems")]
    public List<DhlLineItem> LineItems { get; set; } = new();

    [JsonPropertyName("invoice")]
    public DhlInvoice Invoice { get; set; } = new();

    [JsonPropertyName("exportReason")]
    public string ExportReason { get; set; } = "sale";

    [JsonPropertyName("exportReasonType")]
    public string ExportReasonType { get; set; } = "permanent";
}

public class DhlLineItem
{
    [JsonPropertyName("number")]
    public int Number { get; set; }

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("price")]
    public decimal Price { get; set; }

    [JsonPropertyName("priceCurrency")]
    public string PriceCurrency { get; set; } = "CAD";

    [JsonPropertyName("quantity")]
    public DhlLineItemQuantity Quantity { get; set; } = new();

    [JsonPropertyName("commodityCodes")]
    public List<DhlCommodityCode> CommodityCodes { get; set; } = new();

    [JsonPropertyName("manufacturerCountry")]
    public string ManufacturerCountry { get; set; } = "";

    [JsonPropertyName("weight")]
    public DhlItemWeight Weight { get; set; } = new();
}

public class DhlLineItemQuantity
{
    [JsonPropertyName("value")]
    public decimal Value { get; set; }

    [JsonPropertyName("unitOfMeasurement")]
    public string UnitOfMeasurement { get; set; } = "PCS";
}

public class DhlCommodityCode
{
    [JsonPropertyName("typeCode")]
    public string TypeCode { get; set; } = "outbound";

    [JsonPropertyName("value")]
    public string Value { get; set; } = "";
}

public class DhlItemWeight
{
    [JsonPropertyName("netValue")]
    public decimal NetValue { get; set; }

    [JsonPropertyName("grossValue")]
    public decimal GrossValue { get; set; }
}

public class DhlInvoice
{
    [JsonPropertyName("number")]
    public string Number { get; set; } = "";

    [JsonPropertyName("date")]
    public string Date { get; set; } = "";
}

// ─── Response ──────────────────────────────────────────────────────────────

public class DhlShipmentResponse
{
    [JsonPropertyName("shipmentTrackingNumber")]
    public string? ShipmentTrackingNumber { get; set; }

    [JsonPropertyName("cancelPickupUrl")]
    public string? CancelPickupUrl { get; set; }

    [JsonPropertyName("trackingUrl")]
    public string? TrackingUrl { get; set; }

    [JsonPropertyName("dispatchConfirmationNumber")]
    public string? DispatchConfirmationNumber { get; set; }

    [JsonPropertyName("packages")]
    public List<DhlShipmentPackageResult> Packages { get; set; } = new();

    [JsonPropertyName("documents")]
    public List<DhlDocument> Documents { get; set; } = new();
}

public class DhlShipmentPackageResult
{
    [JsonPropertyName("referenceNumber")]
    public int ReferenceNumber { get; set; }

    [JsonPropertyName("trackingNumber")]
    public string? TrackingNumber { get; set; }
}

public class DhlDocument
{
    [JsonPropertyName("typeCode")]
    public string TypeCode { get; set; } = "";

    [JsonPropertyName("imageFormat")]
    public string ImageFormat { get; set; } = "";

    [JsonPropertyName("content")]
    public string Content { get; set; } = "";

    [JsonPropertyName("format")]
    public string Format { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";
}
