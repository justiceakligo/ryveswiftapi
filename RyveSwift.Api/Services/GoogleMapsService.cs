using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using RyveSwift.Api.Dtos;

namespace RyveSwift.Api.Services;

public class GoogleMapsService
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly string[] NearbyDhlKeywords =
    [
        "DHL Service Point",
        "DHL Express",
        "DHL Drop Off",
        "DHL Authorized Shipping Center"
    ];

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ConfigService _config;
    private readonly ILogger<GoogleMapsService> _logger;

    public GoogleMapsService(
        IHttpClientFactory httpClientFactory,
        ConfigService config,
        ILogger<GoogleMapsService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _config = config;
        _logger = logger;
    }

    public GoogleMapsRuntimeConfig GetRuntimeConfig()
    {
        var defaultRadius = Math.Clamp(_config.GetInt("GOOGLE_MAPS_DEFAULT_RADIUS_METERS", 10000), 500, 50000);
        var maxDhlPoints = Math.Clamp(_config.GetInt("GOOGLE_MAPS_MAX_DHL_POINTS", 10), 1, 20);
        var browserKey = _config.Get("GOOGLE_MAPS_BROWSER_KEY", "");
        var serverKey = _config.Get("GOOGLE_MAPS_SERVER_KEY", "");
        return new GoogleMapsRuntimeConfig(
            Enabled: GetBool("GOOGLE_MAPS_ENABLED", false),
            BrowserKey: browserKey,
            ServerKey: serverKey,
            PlacesBaseUrl: _config.Get("GOOGLE_MAPS_PLACES_BASE_URL", "https://places.googleapis.com/v1").TrimEnd('/'),
            MapId: Clean(_config.Get("GOOGLE_MAPS_MAP_ID", "")),
            CountryRestrictions: ParseCsv(_config.Get("GOOGLE_MAPS_COUNTRY_RESTRICTIONS", "CA,US,GH,NG,KE,ZA,ET")),
            DefaultRadiusMeters: defaultRadius,
            MaxDhlPoints: maxDhlPoints,
            DefaultDhlPointPhone: _config.Get("GOOGLE_MAPS_DEFAULT_DHL_POINT_PHONE", "+18002255345"));
    }

    public async Task<DhlPointSearchResponse> FindDhlPointsAsync(
        decimal? lat,
        decimal? lng,
        string? query,
        string? city,
        string? country,
        int? radiusMeters,
        int? limit,
        CancellationToken ct = default)
    {
        var cfg = GetRuntimeConfig();
        if (!cfg.Enabled)
        {
            return new DhlPointSearchResponse(
                "disabled",
                "google_places",
                [],
                "Google Maps support is not enabled.");
        }

        var key = GetServerKey(cfg);
        if (key is null)
        {
            return new DhlPointSearchResponse(
                "unavailable",
                "google_places",
                [],
                "Google Maps server key is not configured.");
        }

        var hasLocation = lat.HasValue && lng.HasValue;
        var cleanedQuery = Clean(query);
        var cleanedCity = Clean(city);
        if (!hasLocation && string.IsNullOrWhiteSpace(cleanedQuery) && string.IsNullOrWhiteSpace(cleanedCity))
        {
            return new DhlPointSearchResponse(
                "validation_failed",
                "google_places",
                [],
                "Use my location, enter a city, or enter an address/search term to find DHL points.");
        }

        var searchRadius = Math.Clamp(radiusMeters ?? cfg.DefaultRadiusMeters, 500, 50000);
        var maxResults = Math.Clamp(limit ?? cfg.MaxDhlPoints, 1, 20);
        var points = new List<DhlPointSuggestion>();
        var warnings = new List<string>();

        try
        {
            if (hasLocation)
            {
                foreach (var keyword in NearbyDhlKeywords)
                {
                    var response = await SendPlacesRequestAsync(
                        cfg,
                        BuildTextSearchBody(keyword, country, lat, lng, searchRadius, maxResults),
                        key,
                        ct);

                    AddMappedPoints(points, response, cfg.DefaultDhlPointPhone, lat, lng, "google_nearbysearch");
                    AddWarningIfNeeded(warnings, response);

                    if (points.Count >= maxResults)
                        break;
                }
            }

            if (points.Count < maxResults && (!string.IsNullOrWhiteSpace(cleanedQuery) || !string.IsNullOrWhiteSpace(cleanedCity)))
            {
                var textQuery = BuildTextQuery(cleanedQuery, cleanedCity, country);
                var response = await SendPlacesRequestAsync(
                    cfg,
                    BuildTextSearchBody(textQuery, country, lat, lng, searchRadius, maxResults),
                    key,
                    ct);
                AddMappedPoints(points, response, cfg.DefaultDhlPointPhone, lat, lng, string.IsNullOrWhiteSpace(cleanedQuery) ? "google_city_textsearch" : "google_textsearch");
                AddWarningIfNeeded(warnings, response);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Google DHL point search failed.");
            return new DhlPointSearchResponse(
                "unavailable",
                "google_places",
                [],
                "DHL point search is temporarily unavailable.");
        }

        var deduped = Deduplicate(points)
            .OrderBy(p => p.DistanceKm ?? decimal.MaxValue)
            .ThenByDescending(p => p.Rating ?? 0m)
            .Take(maxResults)
            .ToList();

        if (deduped.Count == 0)
        {
            return new DhlPointSearchResponse(
                "no_match",
                "google_places",
                [],
                "No DHL point match found nearby. Try a wider radius or type the public DHL Service Point address.",
                warnings.Count == 0 ? null : warnings.Distinct().ToList());
        }

        return new DhlPointSearchResponse(
            "ok",
            "google_places",
            deduped,
            null,
            warnings.Count == 0 ? null : warnings.Distinct().ToList());
    }

    private async Task<GooglePlacesResponse> SendPlacesRequestAsync(
        GoogleMapsRuntimeConfig cfg,
        GoogleTextSearchRequest payload,
        string apiKey,
        CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient("googlemaps");
        var requestUri = new Uri(cfg.PlacesBaseUrl.TrimEnd('/') + "/places:searchText");
        client.Timeout = TimeSpan.FromSeconds(15);

        using var request = new HttpRequestMessage(HttpMethod.Post, requestUri);
        request.Headers.TryAddWithoutValidation("X-Goog-Api-Key", apiKey);
        request.Headers.TryAddWithoutValidation(
            "X-Goog-FieldMask",
            "places.id,places.displayName,places.formattedAddress,places.shortFormattedAddress,places.location,places.currentOpeningHours,places.rating,places.userRatingCount,places.nationalPhoneNumber,places.internationalPhoneNumber");
        request.Content = JsonContent.Create(payload);

        using var response = await client.SendAsync(request, ct);
        var raw = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Google Places searchText returned {Status}: {Body}", response.StatusCode, raw);
            throw new InvalidOperationException($"Google Places returned HTTP {(int)response.StatusCode}.");
        }

        var parsed = JsonSerializer.Deserialize<GooglePlacesResponse>(raw, JsonOpts)
            ?? new GooglePlacesResponse();

        if (parsed.Status is "REQUEST_DENIED" or "INVALID_REQUEST")
        {
            _logger.LogWarning("Google Places searchText returned {Status}: {Error}", parsed.Status, parsed.ErrorMessage);
        }

        return parsed;
    }

    private static GoogleTextSearchRequest BuildTextSearchBody(
        string textQuery,
        string? country,
        decimal? lat,
        decimal? lng,
        int radiusMeters,
        int maxResults)
    {
        var locationBias = lat.HasValue && lng.HasValue
            ? new GoogleLocationBias
            {
                Circle = new GoogleCircle
                {
                    Center = new GoogleLatLng
                    {
                        Latitude = lat.Value,
                        Longitude = lng.Value
                    },
                    Radius = Math.Clamp(radiusMeters, 500, 50000)
                }
            }
            : null;

        return new GoogleTextSearchRequest
        {
            TextQuery = textQuery,
            MaxResultCount = Math.Clamp(maxResults, 1, 20),
            RegionCode = NormalizeRegionCode(country),
            LocationBias = locationBias,
            IncludePureServiceAreaBusinesses = false
        };
    }

    private static void AddMappedPoints(
        List<DhlPointSuggestion> target,
        GooglePlacesResponse response,
        string defaultPhone,
        decimal? originLat,
        decimal? originLng,
        string source)
    {
        if (response.Places is null || response.Status is "REQUEST_DENIED" or "INVALID_REQUEST")
            return;

        foreach (var result in response.Places)
        {
            var placeId = Clean(result.Id);
            var name = Clean(result.DisplayName?.Text);
            var address = Clean(result.FormattedAddress) ?? Clean(result.ShortFormattedAddress);
            var pointLat = result.Location?.Latitude;
            var pointLng = result.Location?.Longitude;

            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(address) ||
                !pointLat.HasValue || !pointLng.HasValue)
                continue;

            target.Add(new DhlPointSuggestion(
                Id: placeId ?? $"google-{NormalizeId(name + "-" + address)}",
                GooglePlaceId: placeId,
                Name: name,
                Address: address,
                Phone: Clean(result.InternationalPhoneNumber) ?? Clean(result.NationalPhoneNumber) ??
                    (string.IsNullOrWhiteSpace(defaultPhone) ? null : defaultPhone.Trim()),
                Latitude: pointLat.Value,
                Longitude: pointLng.Value,
                DistanceKm: originLat.HasValue && originLng.HasValue
                    ? DistanceKm(originLat.Value, originLng.Value, pointLat.Value, pointLng.Value)
                    : null,
                OpenNow: result.CurrentOpeningHours?.OpenNow,
                Rating: result.Rating,
                UserRatingsTotal: result.UserRatingCount,
                Source: source));
        }
    }

    private static List<DhlPointSuggestion> Deduplicate(IEnumerable<DhlPointSuggestion> points) =>
        points
            .GroupBy(p => p.GooglePlaceId ?? NormalizeId(p.Name + "|" + p.Address))
            .Select(g => g.OrderBy(p => p.DistanceKm ?? decimal.MaxValue).First())
            .ToList();

    private static void AddWarningIfNeeded(List<string> warnings, GooglePlacesResponse response)
    {
        if (response.Status == "OVER_QUERY_LIMIT")
            warnings.Add("Google Places rate limit was reached.");
        if (response.Status == "REQUEST_DENIED")
            warnings.Add("Google Places rejected the configured key or API restrictions.");
        if (response.Status == "INVALID_REQUEST")
            warnings.Add("Google Places rejected the search request.");
        if (!string.IsNullOrWhiteSpace(response.ErrorMessage) && response.Status != "OK" && response.Status != "ZERO_RESULTS")
            warnings.Add(response.Status ?? "Google Places returned a warning.");
    }

    private static string BuildTextQuery(string? query, string? city, string? country)
    {
        var searchText = Clean(query) ?? Clean(city) ?? "";
        var cleaned = searchText.Contains("DHL", StringComparison.OrdinalIgnoreCase)
            ? searchText
            : $"DHL Service Point {searchText}";

        if (!string.IsNullOrWhiteSpace(city) && !cleaned.Contains(city, StringComparison.OrdinalIgnoreCase))
            cleaned += $" {city.Trim()}";

        if (!string.IsNullOrWhiteSpace(country) && !cleaned.Contains(country, StringComparison.OrdinalIgnoreCase))
            cleaned += $" {country.Trim().ToUpperInvariant()}";

        return cleaned;
    }

    private static string? NormalizeRegionCode(string? country)
    {
        var normalized = Clean(country)?.ToUpperInvariant();
        return normalized is { Length: 2 } && normalized.All(char.IsLetter) ? normalized : null;
    }

    private static string? GetServerKey(GoogleMapsRuntimeConfig cfg)
    {
        var server = CleanSecret(cfg.ServerKey);
        if (server is not null)
            return server;

        return CleanSecret(cfg.BrowserKey);
    }

    public static bool IsPlaceholder(string? value) =>
        string.IsNullOrWhiteSpace(value) ||
        value.StartsWith("PLACEHOLDER_", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("CHANGE_ME", StringComparison.OrdinalIgnoreCase);

    private bool GetBool(string key, bool defaultValue)
    {
        var value = _config.Get(key, defaultValue ? "true" : "false");
        return bool.TryParse(value, out var parsed) ? parsed : defaultValue;
    }

    private static string? CleanSecret(string? value) =>
        IsPlaceholder(value) ? null : value!.Trim();

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static IReadOnlyList<string> ParseCsv(string value) =>
        value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(v => v.ToUpperInvariant())
            .Where(v => v.Length == 2)
            .Distinct()
            .ToList();

    private static string ToInvariant(decimal value) =>
        value.ToString(CultureInfo.InvariantCulture);

    private static string NormalizeId(string value)
    {
        var normalized = new string(value.Trim().ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '-')
            .ToArray());

        while (normalized.Contains("--", StringComparison.Ordinal))
            normalized = normalized.Replace("--", "-", StringComparison.Ordinal);

        return normalized.Trim('-');
    }

    private static decimal DistanceKm(decimal lat1, decimal lon1, decimal lat2, decimal lon2)
    {
        const double earthRadiusKm = 6371.0088;
        var dLat = DegreesToRadians((double)(lat2 - lat1));
        var dLon = DegreesToRadians((double)(lon2 - lon1));
        var rLat1 = DegreesToRadians((double)lat1);
        var rLat2 = DegreesToRadians((double)lat2);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(rLat1) * Math.Cos(rLat2) *
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return Math.Round((decimal)(earthRadiusKm * c), 2);
    }

    private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180;

    private sealed record GooglePlacesResponse
    {
        [JsonPropertyName("status")]
        public string? Status { get; init; }

        [JsonPropertyName("error_message")]
        public string? ErrorMessage { get; init; }

        [JsonPropertyName("places")]
        public List<GooglePlaceResult>? Places { get; init; }
    }

    private sealed record GooglePlaceResult
    {
        [JsonPropertyName("id")]
        public string? Id { get; init; }

        [JsonPropertyName("displayName")]
        public GoogleLocalizedText? DisplayName { get; init; }

        [JsonPropertyName("name")]
        public string? Name { get; init; }

        [JsonPropertyName("formatted_address")]
        public string? FormattedAddress { get; init; }

        [JsonPropertyName("formattedAddress")]
        public string? FormattedAddressNew
        {
            init => FormattedAddress = value;
        }

        [JsonPropertyName("shortFormattedAddress")]
        public string? ShortFormattedAddress { get; init; }

        [JsonPropertyName("location")]
        public GoogleLatLng? Location { get; init; }

        [JsonPropertyName("currentOpeningHours")]
        public GoogleOpeningHours? CurrentOpeningHours { get; init; }

        [JsonPropertyName("rating")]
        public decimal? Rating { get; init; }

        [JsonPropertyName("userRatingCount")]
        public int? UserRatingCount { get; init; }

        [JsonPropertyName("nationalPhoneNumber")]
        public string? NationalPhoneNumber { get; init; }

        [JsonPropertyName("internationalPhoneNumber")]
        public string? InternationalPhoneNumber { get; init; }
    }

    private sealed record GoogleLocalizedText
    {
        [JsonPropertyName("text")]
        public string? Text { get; init; }
    }

    private sealed record GoogleLatLng
    {
        [JsonPropertyName("latitude")]
        public decimal Latitude { get; init; }

        [JsonPropertyName("longitude")]
        public decimal Longitude { get; init; }
    }

    private sealed record GoogleOpeningHours
    {
        [JsonPropertyName("open_now")]
        public bool? OpenNow { get; init; }

        [JsonPropertyName("openNow")]
        public bool? OpenNowNew
        {
            init => OpenNow = value;
        }
    }

    private sealed record GoogleTextSearchRequest
    {
        [JsonPropertyName("textQuery")]
        public string TextQuery { get; init; } = "";

        [JsonPropertyName("maxResultCount")]
        public int MaxResultCount { get; init; }

        [JsonPropertyName("regionCode")]
        public string? RegionCode { get; init; }

        [JsonPropertyName("locationBias")]
        public GoogleLocationBias? LocationBias { get; init; }

        [JsonPropertyName("includePureServiceAreaBusinesses")]
        public bool IncludePureServiceAreaBusinesses { get; init; }
    }

    private sealed record GoogleLocationBias
    {
        [JsonPropertyName("circle")]
        public GoogleCircle Circle { get; init; } = new();
    }

    private sealed record GoogleCircle
    {
        [JsonPropertyName("center")]
        public GoogleLatLng Center { get; init; } = new();

        [JsonPropertyName("radius")]
        public decimal Radius { get; init; }
    }
}

public record GoogleMapsRuntimeConfig(
    bool Enabled,
    string BrowserKey,
    string ServerKey,
    string PlacesBaseUrl,
    string? MapId,
    IReadOnlyList<string> CountryRestrictions,
    int DefaultRadiusMeters,
    int MaxDhlPoints,
    string DefaultDhlPointPhone);
