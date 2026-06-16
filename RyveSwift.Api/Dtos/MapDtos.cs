namespace RyveSwift.Api.Dtos;

public record PublicMapsConfigResponse(
    bool Enabled,
    bool BrowserKeyConfigured,
    string? GoogleMapsBrowserKey,
    string? MapId,
    IReadOnlyList<string> CountryRestrictions,
    int DefaultRadiusMeters,
    int MaxDhlPoints,
    string DefaultDhlPointPhone,
    bool PlacesAutocompleteEnabled,
    bool MapDragSelectionEnabled);

public record AdminMapsConfigResponse(
    bool Enabled,
    string PlacesBaseUrl,
    bool BrowserKeyConfigured,
    string? BrowserKey,
    bool ServerKeyConfigured,
    string? ServerKey,
    string? MapId,
    IReadOnlyList<string> CountryRestrictions,
    int DefaultRadiusMeters,
    int MaxDhlPoints,
    string DefaultDhlPointPhone);

public record AdminMapsConfigUpdateRequest(
    bool? Enabled,
    string? BrowserKey,
    string? ServerKey,
    string? PlacesBaseUrl,
    string? MapId,
    string? CountryRestrictions,
    int? DefaultRadiusMeters,
    int? MaxDhlPoints,
    string? DefaultDhlPointPhone);

public record DhlPointSearchResponse(
    string Status,
    string Provider,
    IReadOnlyList<DhlPointSuggestion> Points,
    string? Message = null,
    IReadOnlyList<string>? Warnings = null);

public record DhlPointSuggestion(
    string Id,
    string? GooglePlaceId,
    string Name,
    string Address,
    string? Phone,
    decimal Latitude,
    decimal Longitude,
    decimal? DistanceKm,
    bool? OpenNow,
    decimal? Rating,
    int? UserRatingsTotal,
    string Source);
