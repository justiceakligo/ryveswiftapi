using RyveSwift.Api.Common;
using RyveSwift.Api.Dtos;
using RyveSwift.Api.Services;

namespace RyveSwift.Api.Endpoints;

public static class MapsEndpoints
{
    public static void MapMapsEndpoints(this WebApplication app)
    {
        app.MapGet("/api/maps/config", GetPublicConfig)
            .WithTags("Maps")
            .WithName("GetPublicMapsConfig")
            .WithSummary("Get public Google Maps frontend configuration")
            .RequireRateLimiting("general")
            .AllowAnonymous();
    }

    private static IResult GetPublicConfig(GoogleMapsService maps)
    {
        var cfg = maps.GetRuntimeConfig();
        var browserKeyConfigured = !GoogleMapsService.IsPlaceholder(cfg.BrowserKey);
        return Results.Ok(new PublicMapsConfigResponse(
            cfg.Enabled && browserKeyConfigured,
            browserKeyConfigured,
            cfg.Enabled && browserKeyConfigured ? cfg.BrowserKey : null,
            cfg.MapId,
            cfg.CountryRestrictions,
            cfg.DefaultRadiusMeters,
            cfg.MaxDhlPoints,
            cfg.DefaultDhlPointPhone,
            cfg.Enabled && browserKeyConfigured,
            cfg.Enabled && browserKeyConfigured));
    }
}

public static class AdminMapsEndpoints
{
    public static void MapAdminMapsEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/admin/maps")
            .WithTags("Admin Maps")
            .RequireAuthorization("AdminOnly");

        group.MapGet("/config", GetConfig)
            .WithName("AdminGetMapsConfig")
            .WithSummary("Get Google Maps and Places configuration status");

        group.MapPut("/config", UpdateConfig)
            .WithName("AdminUpdateMapsConfig")
            .WithSummary("Update Google Maps and Places configuration");
    }

    private static IResult GetConfig(GoogleMapsService maps) =>
        Results.Ok(BuildConfigResponse(maps.GetRuntimeConfig()));

    private static async Task<IResult> UpdateConfig(
        AdminMapsConfigUpdateRequest req,
        ConfigService config,
        GoogleMapsService maps)
    {
        if (req.Enabled.HasValue)
            await config.SetAsync("GOOGLE_MAPS_ENABLED", req.Enabled.Value ? "true" : "false");

        if (req.BrowserKey is not null)
            await config.SetAsync("GOOGLE_MAPS_BROWSER_KEY", req.BrowserKey.Trim());

        if (req.ServerKey is not null)
            await config.SetAsync("GOOGLE_MAPS_SERVER_KEY", req.ServerKey.Trim());

        if (!string.IsNullOrWhiteSpace(req.PlacesBaseUrl))
            await config.SetAsync("GOOGLE_MAPS_PLACES_BASE_URL", req.PlacesBaseUrl.Trim().TrimEnd('/'));

        if (req.MapId is not null)
            await config.SetAsync("GOOGLE_MAPS_MAP_ID", req.MapId.Trim());

        if (req.CountryRestrictions is not null)
            await config.SetAsync("GOOGLE_MAPS_COUNTRY_RESTRICTIONS", NormalizeCountries(req.CountryRestrictions));

        if (req.DefaultRadiusMeters.HasValue)
            await config.SetAsync("GOOGLE_MAPS_DEFAULT_RADIUS_METERS", Math.Clamp(req.DefaultRadiusMeters.Value, 500, 50000).ToString());

        if (req.MaxDhlPoints.HasValue)
            await config.SetAsync("GOOGLE_MAPS_MAX_DHL_POINTS", Math.Clamp(req.MaxDhlPoints.Value, 1, 20).ToString());

        if (req.DefaultDhlPointPhone is not null)
            await config.SetAsync("GOOGLE_MAPS_DEFAULT_DHL_POINT_PHONE", req.DefaultDhlPointPhone.Trim());

        return Results.Ok(BuildConfigResponse(maps.GetRuntimeConfig()));
    }

    private static AdminMapsConfigResponse BuildConfigResponse(GoogleMapsRuntimeConfig cfg) => new(
        cfg.Enabled,
        cfg.PlacesBaseUrl,
        !GoogleMapsService.IsPlaceholder(cfg.BrowserKey),
        MaskKey(cfg.BrowserKey),
        !GoogleMapsService.IsPlaceholder(cfg.ServerKey),
        MaskKey(cfg.ServerKey),
        cfg.MapId,
        cfg.CountryRestrictions,
        cfg.DefaultRadiusMeters,
        cfg.MaxDhlPoints,
        cfg.DefaultDhlPointPhone);

    private static string NormalizeCountries(string countries)
    {
        var normalized = countries
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(c => c.ToUpperInvariant())
            .Where(c => c.Length == 2 && c.All(char.IsLetter))
            .Distinct()
            .ToList();

        return normalized.Count == 0 ? "" : string.Join(',', normalized);
    }

    private static string? MaskKey(string? key)
    {
        if (GoogleMapsService.IsPlaceholder(key))
            return null;

        var trimmed = key!.Trim();
        return trimmed.Length <= 10
            ? "***"
            : $"{trimmed[..6]}...{trimmed[^4..]}";
    }
}
