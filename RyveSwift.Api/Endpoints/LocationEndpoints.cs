using RyveSwift.Api.Common;
using RyveSwift.Api.Services;

namespace RyveSwift.Api.Endpoints;

public static class LocationEndpoints
{
    public static void MapLocationEndpoints(this WebApplication app)
    {
        app.MapGet("/api/locations/postal-suggestions", GetPostalSuggestions)
            .WithTags("Locations")
            .WithName("GetPostalSuggestions")
            .WithSummary("Suggest postal or DHL service-area values for quote addresses")
            .RequireRateLimiting("general")
            .AllowAnonymous();

        app.MapGet("/api/locations/dhl-points", GetDhlPoints)
            .WithTags("Locations")
            .WithName("GetDhlPoints")
            .WithSummary("Find nearby DHL Service Points using Google Places")
            .RequireRateLimiting("general")
            .AllowAnonymous();
    }

    private static async Task<IResult> GetPostalSuggestions(
        string? country,
        string? city,
        string? postalCode,
        string? role,
        LocationSuggestionService suggestions,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(country) || country.Trim().Length != 2)
        {
            return Results.BadRequest(new ApiError(
                "validation_failed",
                "Some location details are invalid.",
                new[] { new FieldError("country", "Valid 2-letter country code is required.") }));
        }

        var response = await suggestions.GetPostalSuggestionsAsync(
            country,
            city,
            postalCode,
            role,
            cancellationToken);

        return Results.Ok(response);
    }

    private static async Task<IResult> GetDhlPoints(
        decimal? lat,
        decimal? lng,
        string? query,
        string? city,
        string? country,
        int? radiusMeters,
        int? limit,
        GoogleMapsService maps,
        CancellationToken cancellationToken)
    {
        var hasLat = lat.HasValue;
        var hasLng = lng.HasValue;
        if (hasLat != hasLng)
        {
            return Results.BadRequest(new ApiError(
                "validation_failed",
                "Some DHL point search details are invalid.",
                new[] { new FieldError("location", "Both lat and lng are required when using location search.") }));
        }

        if (lat is < -90 or > 90)
            return Results.BadRequest(new ApiError("validation_failed", "Latitude must be between -90 and 90."));
        if (lng is < -180 or > 180)
            return Results.BadRequest(new ApiError("validation_failed", "Longitude must be between -180 and 180."));

        var response = await maps.FindDhlPointsAsync(
            lat,
            lng,
            query,
            city,
            country,
            radiusMeters,
            limit,
            cancellationToken);

        return response.Status switch
        {
            "validation_failed" => Results.BadRequest(new ApiError("validation_failed", response.Message ?? "DHL point search is invalid.")),
            "disabled" => Results.Json(response, statusCode: StatusCodes.Status503ServiceUnavailable),
            "unavailable" => Results.Json(response, statusCode: StatusCodes.Status503ServiceUnavailable),
            _ => Results.Ok(response)
        };
    }
}
