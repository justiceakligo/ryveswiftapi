namespace RyveSwift.Api.Dtos;

public record LocationSuggestionResponse(
    string Status,
    LocationSuggestion? Typed,
    IReadOnlyList<LocationSuggestion> Recommendations,
    IReadOnlyList<string>? Warnings = null,
    string? Message = null);

public record LocationSuggestion(
    string? Id,
    string Kind,
    string Label,
    string Country,
    string City,
    string PostalCode,
    string? ServiceAreaCode = null,
    string? Province = null,
    decimal? Confidence = null,
    string? Source = null,
    bool? CarrierRecognized = null,
    IReadOnlyList<string>? Warnings = null);
