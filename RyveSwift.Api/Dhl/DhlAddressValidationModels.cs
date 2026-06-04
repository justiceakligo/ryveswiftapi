using System.Text.Json.Serialization;

namespace RyveSwift.Api.Dhl;

public class DhlAddressValidationResponse
{
    [JsonPropertyName("address")]
    public List<DhlValidatedAddress> Address { get; set; } = new();

    [JsonPropertyName("addresses")]
    public List<DhlValidatedAddress> Addresses { get; set; } = new();

    [JsonPropertyName("warnings")]
    public List<string> Warnings { get; set; } = new();

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonIgnore]
    public IReadOnlyList<DhlValidatedAddress> AllAddresses =>
        Address is { Count: > 0 } ? Address : Addresses ?? [];
}

public class DhlValidatedAddress
{
    [JsonPropertyName("countryCode")]
    public string CountryCode { get; set; } = "";

    [JsonPropertyName("postalCode")]
    public string PostalCode { get; set; } = "";

    [JsonPropertyName("cityName")]
    public string CityName { get; set; } = "";

    [JsonPropertyName("countyName")]
    public string? CountyName { get; set; }

    [JsonPropertyName("provinceCode")]
    public string? ProvinceCode { get; set; }

    [JsonPropertyName("provinceName")]
    public string? ProvinceName { get; set; }

    [JsonPropertyName("serviceArea")]
    public DhlAddressServiceArea? ServiceArea { get; set; }
}

public class DhlAddressServiceArea
{
    [JsonPropertyName("code")]
    public string? Code { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }
}
