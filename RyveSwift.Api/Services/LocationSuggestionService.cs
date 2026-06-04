using RyveSwift.Api.Dhl;
using RyveSwift.Api.Dtos;

namespace RyveSwift.Api.Services;

public class LocationSuggestionService
{
    private readonly DhlService _dhl;
    private readonly ILogger<LocationSuggestionService> _logger;

    private static readonly LocationSuggestion[] CuratedHints =
    [
        new("postal-gh-accra", "recommended", "Accra, Ghana", "GH", "Accra", "GA184", "GA184", null, 0.74m, "curated_postal_hint", false),
        new("postal-ng-lagos", "recommended", "Lagos, Nigeria", "NG", "Lagos", "100001", "100001", null, 0.74m, "curated_postal_hint", false)
    ];

    public LocationSuggestionService(DhlService dhl, ILogger<LocationSuggestionService> logger)
    {
        _dhl = dhl;
        _logger = logger;
    }

    public async Task<LocationSuggestionResponse> GetPostalSuggestionsAsync(
        string country,
        string? city,
        string? postalCode,
        string? role,
        CancellationToken cancellationToken = default)
    {
        var countryCode = NormalizeCountry(country);
        var cityName = Clean(city);
        var typedPostalCode = Clean(postalCode);
        var normalizedRole = NormalizeRole(role);
        var warnings = new List<string>();
        var typedWarnings = new List<string>();
        var recommendations = new List<LocationSuggestion>();
        var providerUnavailable = false;

        if (!string.IsNullOrWhiteSpace(role) && normalizedRole != role.Trim().ToLowerInvariant())
            warnings.Add("Unknown role was treated as destination.");

        LocationSuggestion? typed = null;
        if (!string.IsNullOrWhiteSpace(cityName) || !string.IsNullOrWhiteSpace(typedPostalCode))
        {
            typed = new LocationSuggestion(
                null,
                "typed",
                "Use what I typed",
                countryCode,
                cityName ?? "",
                typedPostalCode ?? "",
                Source: "user",
                CarrierRecognized: false);
        }

        if (!string.IsNullOrWhiteSpace(typedPostalCode))
        {
            try
            {
                var dhlResult = await _dhl.ValidateAddressAsync(
                    countryCode,
                    typedPostalCode,
                    cityName,
                    normalizedRole,
                    cancellationToken);

                recommendations.AddRange(MapDhlRecommendations(dhlResult, countryCode));
                warnings.AddRange((dhlResult.Warnings ?? []).Where(w => !string.IsNullOrWhiteSpace(w)));
            }
            catch (DhlException ex) when (ex.IsClientError)
            {
                _logger.LogInformation(ex, "DHL address validation did not find a match for {Country} {PostalCode}.", countryCode, typedPostalCode);
            }
            catch (Exception ex)
            {
                providerUnavailable = true;
                warnings.Add("DHL address validation is temporarily unavailable.");
                _logger.LogWarning(ex, "DHL address validation unavailable for {Country} {PostalCode}.", countryCode, typedPostalCode);
            }
        }

        AddCuratedHints(recommendations, countryCode, cityName, typedPostalCode);
        recommendations = Deduplicate(recommendations);

        var matchingPostalDifferentCity = recommendations.FirstOrDefault(r =>
            !string.IsNullOrWhiteSpace(typedPostalCode) &&
            PostalMatches(countryCode, typedPostalCode, r.PostalCode) &&
            !string.IsNullOrWhiteSpace(cityName) &&
            !CityMatches(cityName, r.City));

        if (matchingPostalDifferentCity is not null)
        {
            typedWarnings.Add($"{typedPostalCode} appears to be in {matchingPostalDifferentCity.City}, not {cityName}.");
            warnings.Add("The selected city and postal code appear to describe different places.");
        }

        if (typed is not null)
        {
            var typedRecognized = recommendations.Any(r =>
                PostalMatches(countryCode, typed.PostalCode, r.PostalCode) &&
                (string.IsNullOrWhiteSpace(typed.City) || CityMatches(typed.City, r.City)) &&
                r.CarrierRecognized == true);

            typed = typed with
            {
                CarrierRecognized = typedRecognized,
                Warnings = typedWarnings.Count == 0 ? null : typedWarnings
            };
        }

        var status = ResolveStatus(recommendations, warnings, providerUnavailable);
        var message = status == "no_match"
            ? NoMatchMessage(countryCode, cityName)
            : status == "unavailable"
                ? "Location suggestions are temporarily unavailable."
                : null;

        return new LocationSuggestionResponse(
            status,
            typed,
            recommendations,
            warnings.Count == 0 ? null : warnings.Distinct().ToList(),
            message);
    }

    private static IReadOnlyList<LocationSuggestion> MapDhlRecommendations(
        DhlAddressValidationResponse dhlResult,
        string fallbackCountry)
    {
        return dhlResult.AllAddresses
            .Where(a => !string.IsNullOrWhiteSpace(a.PostalCode) || !string.IsNullOrWhiteSpace(a.CityName))
            .Select((a, index) =>
            {
                var country = NormalizeCountry(string.IsNullOrWhiteSpace(a.CountryCode) ? fallbackCountry : a.CountryCode);
                var city = Clean(a.CityName) ?? "";
                var province = Clean(a.ProvinceCode) ?? Clean(a.ProvinceName);
                var serviceAreaCode = Clean(a.ServiceArea?.Code);
                var postal = Clean(a.PostalCode) ?? serviceAreaCode ?? "";
                var labelParts = new[] { city, province, CountryName(country) }.Where(p => !string.IsNullOrWhiteSpace(p));

                return new LocationSuggestion(
                    $"dhl-{country.ToLowerInvariant()}-{NormalizeId(city)}-{index + 1}",
                    "recommended",
                    string.Join(", ", labelParts),
                    country,
                    city,
                    postal,
                    serviceAreaCode,
                    province,
                    0.98m,
                    "dhl_address_validate",
                    true);
            })
            .ToList();
    }

    private static void AddCuratedHints(
        List<LocationSuggestion> recommendations,
        string countryCode,
        string? cityName,
        string? postalCode)
    {
        var matches = CuratedHints.Where(h =>
            h.Country.Equals(countryCode, StringComparison.OrdinalIgnoreCase) &&
            (string.IsNullOrWhiteSpace(cityName) || CityMatches(cityName, h.City)) &&
            (string.IsNullOrWhiteSpace(postalCode) ||
             CityMatches(postalCode, h.City) ||
             PostalMatches(countryCode, postalCode, h.PostalCode)));

        recommendations.AddRange(matches);
    }

    private static List<LocationSuggestion> Deduplicate(IEnumerable<LocationSuggestion> suggestions)
    {
        return suggestions
            .GroupBy(s => $"{s.Country}|{NormalizePostalKey(s.Country, s.PostalCode)}|{NormalizeText(s.City)}")
            .Select(g => g.OrderByDescending(s => s.CarrierRecognized == true).ThenByDescending(s => s.Confidence ?? 0m).First())
            .OrderByDescending(s => s.CarrierRecognized == true)
            .ThenByDescending(s => s.Confidence ?? 0m)
            .Take(8)
            .ToList();
    }

    private static string ResolveStatus(
        IReadOnlyList<LocationSuggestion> recommendations,
        IReadOnlyList<string> warnings,
        bool providerUnavailable)
    {
        if (providerUnavailable && recommendations.Count == 0)
            return "unavailable";
        if (recommendations.Count == 0)
            return "no_match";
        if (recommendations.Count > 1 || warnings.Count > 0)
            return "ambiguous";
        return "ok";
    }

    private static string NoMatchMessage(string countryCode, string? cityName) =>
        !string.IsNullOrWhiteSpace(cityName)
            ? $"No carrier-recognized service-area code was found for {cityName}."
            : $"No carrier-recognized service-area code was found for {CountryName(countryCode)}.";

    private static string NormalizeRole(string? role) =>
        role?.Trim().Equals("origin", StringComparison.OrdinalIgnoreCase) == true ? "origin" : "destination";

    private static string NormalizeCountry(string country) =>
        string.IsNullOrWhiteSpace(country) ? "" : country.Trim().ToUpperInvariant();

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool CityMatches(string? left, string? right) =>
        NormalizeText(left) == NormalizeText(right);

    private static bool PostalMatches(string countryCode, string? left, string? right)
    {
        var leftKey = NormalizePostalKey(countryCode, left);
        var rightKey = NormalizePostalKey(countryCode, right);
        if (string.IsNullOrWhiteSpace(leftKey) || string.IsNullOrWhiteSpace(rightKey))
            return false;

        if (countryCode.Equals("CA", StringComparison.OrdinalIgnoreCase))
            return leftKey.Length >= 3 && rightKey.Length >= 3 && leftKey[..3] == rightKey[..3];

        return leftKey == rightKey;
    }

    private static string NormalizePostalKey(string countryCode, string? value)
    {
        var normalized = new string((value ?? "").Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        if (countryCode.Equals("US", StringComparison.OrdinalIgnoreCase) && normalized.Length > 5)
            return normalized[..5];
        return normalized;
    }

    private static string NormalizeText(string? value) =>
        new((value ?? "").Trim().ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());

    private static string NormalizeId(string value)
    {
        var normalized = NormalizeText(value).ToLowerInvariant();
        return string.IsNullOrWhiteSpace(normalized) ? "location" : normalized;
    }

    private static string CountryName(string countryCode) => countryCode.ToUpperInvariant() switch
    {
        "CA" => "Canada",
        "US" => "United States",
        "GH" => "Ghana",
        "NG" => "Nigeria",
        _ => countryCode.ToUpperInvariant()
    };
}
