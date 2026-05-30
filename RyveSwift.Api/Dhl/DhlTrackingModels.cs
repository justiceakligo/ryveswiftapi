using System.Text.Json;
using System.Text.Json.Serialization;

namespace RyveSwift.Api.Dhl;

public class DhlTrackingResponse
{
    [JsonPropertyName("shipments")]
    public List<DhlShipmentTracking> Shipments { get; set; } = new();
}

public class DhlShipmentTracking
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("service")]
    public string? Service { get; set; }

    [JsonPropertyName("estimatedDeliveryTime")]
    public string? EstimatedDeliveryDate { get; set; }

    [JsonPropertyName("status")]
    [JsonConverter(typeof(DhlTrackingStatusConverter))]
    public DhlTrackingStatus? Status { get; set; }

    [JsonPropertyName("events")]
    public List<DhlTrackingEvent> Events { get; set; } = new();
}

public class DhlTrackingStatus
{
    [JsonPropertyName("timestamp")]
    public string? Timestamp { get; set; }

    [JsonPropertyName("location")]
    public DhlTrackingLocation? Location { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("remark")]
    public string? Remark { get; set; }

    [JsonPropertyName("nextSteps")]
    public string? NextSteps { get; set; }

    [JsonPropertyName("statusCode")]
    public string? StatusCode { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }
}

public class DhlTrackingEvent
{
    [JsonPropertyName("timestamp")]
    public string? Timestamp { get; set; }

    [JsonPropertyName("location")]
    public DhlTrackingLocation? Location { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("serviceArea")]
    public List<DhlServiceArea>? ServiceArea { get; set; }
}

public class DhlTrackingLocation
{
    [JsonPropertyName("address")]
    public DhlTrackingAddress? Address { get; set; }
}

public class DhlTrackingAddress
{
    [JsonPropertyName("addressLocality")]
    public string? AddressLocality { get; set; }

    [JsonPropertyName("countryCode")]
    public string? CountryCode { get; set; }
}

// Handles DHL returning status as either a plain string or a full object
public class DhlTrackingStatusConverter : JsonConverter<DhlTrackingStatus?>
{
    public override DhlTrackingStatus? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            var s = reader.GetString();
            return new DhlTrackingStatus { StatusCode = s, Status = s };
        }
        if (reader.TokenType == JsonTokenType.StartObject)
        {
            using var doc = JsonDocument.ParseValue(ref reader);
            var inner = new JsonSerializerOptions(options);
            inner.Converters.Clear(); // avoid infinite recursion
            return JsonSerializer.Deserialize<DhlTrackingStatus>(doc.RootElement.GetRawText(), inner);
        }
        reader.Skip();
        return null;
    }

    public override void Write(Utf8JsonWriter writer, DhlTrackingStatus? value, JsonSerializerOptions options)
        => JsonSerializer.Serialize(writer, value, options);
}

public class DhlServiceArea
{
    [JsonPropertyName("code")]
    public string? Code { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }
}
