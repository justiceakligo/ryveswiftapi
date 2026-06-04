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
}
