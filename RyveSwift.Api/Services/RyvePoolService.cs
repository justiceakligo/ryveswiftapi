using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using RyveSwift.Api.Dtos;

namespace RyveSwift.Api.Services;

public class RyvePoolService
{
    public const string EnvironmentTest = "test";
    public const string EnvironmentProduction = "production";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly HashSet<string> AllowedPackageTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "parcel",
        "document",
        "fragile"
    };

    private static readonly HashSet<string> AllowedDispatchModes = new(StringComparer.OrdinalIgnoreCase)
    {
        "own_fleet",
        "ryvepool_marketplace",
        "overflow"
    };

    private static readonly HashSet<string> AllowedPaymentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "prepaid",
        "cod"
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ConfigService _config;
    private readonly ILogger<RyvePoolService> _logger;

    public RyvePoolService(
        IHttpClientFactory httpClientFactory,
        ConfigService config,
        ILogger<RyvePoolService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _config = config;
        _logger = logger;
    }

    public RyvePoolRuntimeConfig GetRuntimeConfig()
    {
        var environment = NormalizeEnvironment(_config.Get("RYVEPOOL_ENVIRONMENT", EnvironmentTest));
        return new RyvePoolRuntimeConfig(
            Enabled: GetBool("RYVEPOOL_ENABLED", false),
            Environment: environment,
            BaseUrl: _config.Get("RYVEPOOL_BASE_URL", "https://api.ryvepool.com/v1").TrimEnd('/'),
            TimeoutSeconds: Math.Clamp(_config.GetInt("RYVEPOOL_TIMEOUT_SECONDS", 20), 5, 120),
            DefaultRegionCode: _config.Get("RYVEPOOL_DEFAULT_REGION_CODE", "CA-ON"),
            DefaultExternalBranchId: CleanOptional(_config.Get("RYVEPOOL_DEFAULT_EXTERNAL_BRANCH_ID", "")),
            DefaultDispatchMode: NormalizeDispatchMode(_config.Get("RYVEPOOL_DEFAULT_DISPATCH_MODE", "ryvepool_marketplace")),
            DefaultPackageType: NormalizePackageType(_config.Get("RYVEPOOL_DEFAULT_PACKAGE_TYPE", "parcel")),
            WebhookSignatureRequired: GetBool("RYVEPOOL_WEBHOOK_SIGNATURE_REQUIRED", true),
            ScheduledDispatchEnabled: GetBool("RYVEPOOL_SCHEDULED_DISPATCH_ENABLED", true),
            ScheduledDispatchIntervalSeconds: Math.Clamp(_config.GetInt("RYVEPOOL_SCHEDULED_DISPATCH_INTERVAL_SECONDS", 60), 15, 3600),
            PublicKey: GetCredential(environment, publicKey: true),
            SecretKey: GetCredential(environment, publicKey: false),
            TestPublicKey: _config.Get("RYVEPOOL_TEST_PUBLIC_KEY", ""),
            TestSecretKey: _config.Get("RYVEPOOL_TEST_SECRET_KEY", ""),
            TestWebhookSecret: _config.Get("RYVEPOOL_TEST_WEBHOOK_SECRET", ""),
            ProductionPublicKey: _config.Get("RYVEPOOL_PRODUCTION_PUBLIC_KEY", ""),
            ProductionSecretKey: _config.Get("RYVEPOOL_PRODUCTION_SECRET_KEY", ""),
            ProductionWebhookSecret: _config.Get("RYVEPOOL_PRODUCTION_WEBHOOK_SECRET", ""));
    }

    public async Task<RyvePoolJsonEnvelope> GetQuoteAsync(RyvePoolQuoteApiRequest request, CancellationToken ct = default)
    {
        var cfg = EnsureEnabled(authRequired: false);
        using var client = CreateClient(cfg, authenticated: false);
        return await SendJsonAsync<JsonElement>(client, HttpMethod.Post, "partner-integrations/quotes", request, ct);
    }

    public async Task<(RyvePoolDispatchApiResponse Dispatch, string RawJson)> CreateDispatchAsync(
        RyvePoolDispatchApiRequest request,
        CancellationToken ct = default)
    {
        var cfg = EnsureEnabled(authRequired: true);
        using var client = CreateClient(cfg, authenticated: true);
        var envelope = await SendJsonAsync<RyvePoolDispatchApiResponse>(
            client,
            HttpMethod.Post,
            "partner-integrations/dispatches",
            request,
            ct);
        return (envelope.Json.Deserialize<RyvePoolDispatchApiResponse>(JsonOpts) ?? new RyvePoolDispatchApiResponse(), envelope.RawJson);
    }

    public async Task<RyvePoolJsonEnvelope> GetDispatchAsync(string dispatchId, CancellationToken ct = default)
    {
        var cfg = EnsureEnabled(authRequired: true);
        using var client = CreateClient(cfg, authenticated: true);
        return await SendJsonAsync<JsonElement>(
            client,
            HttpMethod.Get,
            $"partner-integrations/dispatches/{Uri.EscapeDataString(dispatchId)}",
            payload: null,
            ct);
    }

    public async Task<RyvePoolJsonEnvelope> LookupByExternalOrderIdAsync(string externalOrderId, CancellationToken ct = default)
    {
        var cfg = EnsureEnabled(authRequired: true);
        using var client = CreateClient(cfg, authenticated: true);
        var path = $"partner-integrations/dispatches?external_order_id={Uri.EscapeDataString(externalOrderId)}";
        return await SendJsonAsync<JsonElement>(client, HttpMethod.Get, path, payload: null, ct);
    }

    public async Task<RyvePoolJsonEnvelope> GetLiveAsync(string dispatchId, CancellationToken ct = default)
    {
        var cfg = EnsureEnabled(authRequired: true);
        using var client = CreateClient(cfg, authenticated: true);
        return await SendJsonAsync<JsonElement>(
            client,
            HttpMethod.Get,
            $"partner-integrations/dispatches/{Uri.EscapeDataString(dispatchId)}/live",
            payload: null,
            ct);
    }

    public async Task<(RyvePoolDispatchApiResponse Dispatch, string RawJson)> UpdateRecipientAsync(
        string dispatchId,
        RyvePoolRecipientUpdateApiRequest request,
        CancellationToken ct = default)
    {
        var cfg = EnsureEnabled(authRequired: true);
        using var client = CreateClient(cfg, authenticated: true);
        var envelope = await SendJsonAsync<RyvePoolDispatchApiResponse>(
            client,
            HttpMethod.Patch,
            $"partner-integrations/dispatches/{Uri.EscapeDataString(dispatchId)}",
            request,
            ct);
        return (envelope.Json.Deserialize<RyvePoolDispatchApiResponse>(JsonOpts) ?? new RyvePoolDispatchApiResponse(), envelope.RawJson);
    }

    public async Task<(RyvePoolDispatchApiResponse Dispatch, string RawJson)> CancelAsync(
        string dispatchId,
        RyvePoolCancelApiRequest request,
        CancellationToken ct = default)
    {
        var cfg = EnsureEnabled(authRequired: true);
        using var client = CreateClient(cfg, authenticated: true);
        var envelope = await SendJsonAsync<RyvePoolDispatchApiResponse>(
            client,
            HttpMethod.Post,
            $"partner-integrations/dispatches/{Uri.EscapeDataString(dispatchId)}/cancel",
            request,
            ct);
        return (envelope.Json.Deserialize<RyvePoolDispatchApiResponse>(JsonOpts) ?? new RyvePoolDispatchApiResponse(), envelope.RawJson);
    }

    public async Task<RyvePoolJsonEnvelope> GetReportSummaryAsync(
        DateTime? from,
        DateTime? to,
        string? branchId,
        string? regionCode,
        CancellationToken ct = default) =>
        await GetReportAsync("partner-integrations/reports/summary", from, to, branchId, regionCode, ct);

    public async Task<RyvePoolJsonEnvelope> GetBranchReportAsync(
        DateTime? from,
        DateTime? to,
        string? branchId,
        string? regionCode,
        CancellationToken ct = default) =>
        await GetReportAsync("partner-integrations/reports/branches", from, to, branchId, regionCode, ct);

    public async Task<RyvePoolJsonEnvelope> SendWebhookTestAsync(CancellationToken ct = default)
    {
        var cfg = EnsureEnabled(authRequired: true);
        using var client = CreateClient(cfg, authenticated: true);
        return await SendJsonAsync<JsonElement>(
            client,
            HttpMethod.Post,
            "partner-integrations/webhooks/test",
            payload: new { },
            ct);
    }

    public async Task<RyvePoolJsonEnvelope> GetWebhookEventsAsync(
        string? eventType,
        string? status,
        int limit,
        CancellationToken ct = default)
    {
        var cfg = EnsureEnabled(authRequired: true);
        using var client = CreateClient(cfg, authenticated: true);
        var query = new List<string> { $"limit={Math.Clamp(limit, 1, 200)}" };
        if (!string.IsNullOrWhiteSpace(eventType))
            query.Add($"event_type={Uri.EscapeDataString(eventType.Trim())}");
        if (!string.IsNullOrWhiteSpace(status))
            query.Add($"status={Uri.EscapeDataString(status.Trim())}");

        return await SendJsonAsync<JsonElement>(
            client,
            HttpMethod.Get,
            "partner-integrations/webhook-events?" + string.Join('&', query),
            payload: null,
            ct);
    }

    public string? GetWebhookSecret(string environment)
    {
        var cfg = GetRuntimeConfig();
        environment = NormalizeEnvironment(environment);
        return environment == EnvironmentProduction
            ? CleanSecret(cfg.ProductionWebhookSecret)
            : CleanSecret(cfg.TestWebhookSecret);
    }

    public IEnumerable<string> GetConfiguredWebhookSecrets()
    {
        var cfg = GetRuntimeConfig();
        var test = CleanSecret(cfg.TestWebhookSecret);
        var production = CleanSecret(cfg.ProductionWebhookSecret);
        if (test is not null) yield return test;
        if (production is not null && production != test) yield return production;
    }

    public static string NormalizeEnvironment(string? environment)
    {
        var value = (environment ?? "").Trim().ToLowerInvariant();
        return value is "prod" or "production" or "live" ? EnvironmentProduction : EnvironmentTest;
    }

    public static string NormalizePackageType(string? packageType)
    {
        var value = string.IsNullOrWhiteSpace(packageType)
            ? "parcel"
            : packageType.Trim().ToLowerInvariant();

        if (value == "food")
            throw new RyvePoolException("unsupported_package_type", "Food delivery is not enabled for this integration.", 422);

        if (!AllowedPackageTypes.Contains(value))
            throw new RyvePoolException("unsupported_package_type", "Package type must be parcel, document, or fragile.", 422);

        return value;
    }

    public static string NormalizeDispatchMode(string? dispatchMode)
    {
        var value = string.IsNullOrWhiteSpace(dispatchMode)
            ? "ryvepool_marketplace"
            : dispatchMode.Trim().ToLowerInvariant();

        if (!AllowedDispatchModes.Contains(value))
            throw new RyvePoolException("unsupported_dispatch_mode", "Dispatch mode must be own_fleet, ryvepool_marketplace, or overflow.", 422);

        return value;
    }

    public static string NormalizePaymentType(string? paymentType)
    {
        var value = string.IsNullOrWhiteSpace(paymentType)
            ? "prepaid"
            : paymentType.Trim().ToLowerInvariant();

        if (!AllowedPaymentTypes.Contains(value))
            throw new RyvePoolException("unsupported_payment_type", "Payment type must be prepaid or cod.", 422);

        return value;
    }

    private async Task<RyvePoolJsonEnvelope> GetReportAsync(
        string path,
        DateTime? from,
        DateTime? to,
        string? branchId,
        string? regionCode,
        CancellationToken ct)
    {
        var query = new List<string>();
        if (from.HasValue)
            query.Add($"from={Uri.EscapeDataString(from.Value.ToUniversalTime().ToString("O"))}");
        if (to.HasValue)
            query.Add($"to={Uri.EscapeDataString(to.Value.ToUniversalTime().ToString("O"))}");
        if (!string.IsNullOrWhiteSpace(branchId))
            query.Add($"branch_id={Uri.EscapeDataString(branchId.Trim())}");
        if (!string.IsNullOrWhiteSpace(regionCode))
            query.Add($"region_code={Uri.EscapeDataString(regionCode.Trim())}");

        var cfg = EnsureEnabled(authRequired: true);
        using var client = CreateClient(cfg, authenticated: true);
        var url = query.Count == 0 ? path : path + "?" + string.Join('&', query);
        return await SendJsonAsync<JsonElement>(client, HttpMethod.Get, url, payload: null, ct);
    }

    private async Task<RyvePoolJsonEnvelope> SendJsonAsync<T>(
        HttpClient client,
        HttpMethod method,
        string relativeUrl,
        object? payload,
        CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, relativeUrl);
        if (payload is not null)
            request.Content = JsonContent.Create(payload, options: JsonOpts);

        using var response = await client.SendAsync(request, ct);
        var raw = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("RyvePool {Method} {Url} returned {Status}: {Body}",
                method.Method,
                relativeUrl,
                response.StatusCode,
                raw);
            throw BuildException(response, raw);
        }

        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(raw) ? "{}" : raw);
        return new RyvePoolJsonEnvelope(doc.RootElement.Clone(), raw);
    }

    private HttpClient CreateClient(RyvePoolRuntimeConfig cfg, bool authenticated)
    {
        var client = _httpClientFactory.CreateClient("ryvepool");
        client.BaseAddress = new Uri(cfg.BaseUrl.TrimEnd('/') + "/");
        client.Timeout = TimeSpan.FromSeconds(cfg.TimeoutSeconds);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (authenticated)
        {
            var token = $"{cfg.PublicKey}:{cfg.SecretKey}";
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return client;
    }

    private RyvePoolRuntimeConfig EnsureEnabled(bool authRequired)
    {
        var cfg = GetRuntimeConfig();
        if (!cfg.Enabled)
            throw new RyvePoolException("ryvepool_disabled", "RyvePool order delivery is not enabled.", 503);

        if (authRequired && (IsPlaceholder(cfg.PublicKey) || IsPlaceholder(cfg.SecretKey)))
            throw new RyvePoolException("ryvepool_credentials_missing", "RyvePool credentials are not configured for the active environment.", 503);

        return cfg;
    }

    private RyvePoolException BuildException(HttpResponseMessage response, string raw)
    {
        var code = "ryvepool_request_failed";
        var message = $"RyvePool request failed with HTTP {(int)response.StatusCode}.";

        try
        {
            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.TryGetProperty("error", out var error) &&
                error.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(error.GetString()))
            {
                code = error.GetString()!;
            }

            if (doc.RootElement.TryGetProperty("message", out var msg) &&
                msg.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(msg.GetString()))
            {
                message = msg.GetString()!;
            }
        }
        catch (JsonException)
        {
            if (!string.IsNullOrWhiteSpace(raw))
                message = raw.Length > 500 ? raw[..500] : raw;
        }

        return new RyvePoolException(code, message, (int)response.StatusCode, raw);
    }

    private string GetCredential(string environment, bool publicKey)
    {
        var prefix = environment == EnvironmentProduction ? "RYVEPOOL_PRODUCTION" : "RYVEPOOL_TEST";
        return _config.Get(publicKey ? $"{prefix}_PUBLIC_KEY" : $"{prefix}_SECRET_KEY", "");
    }

    private bool GetBool(string key, bool defaultValue)
    {
        var value = _config.Get(key, defaultValue ? "true" : "false");
        return bool.TryParse(value, out var parsed) ? parsed : defaultValue;
    }

    private static string? CleanOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? CleanSecret(string? value) =>
        IsPlaceholder(value) ? null : value!.Trim();

    public static bool IsPlaceholder(string? value) =>
        string.IsNullOrWhiteSpace(value) ||
        value.StartsWith("PLACEHOLDER_", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("CHANGE_ME", StringComparison.OrdinalIgnoreCase);
}

public record RyvePoolRuntimeConfig(
    bool Enabled,
    string Environment,
    string BaseUrl,
    int TimeoutSeconds,
    string DefaultRegionCode,
    string? DefaultExternalBranchId,
    string DefaultDispatchMode,
    string DefaultPackageType,
    bool WebhookSignatureRequired,
    bool ScheduledDispatchEnabled,
    int ScheduledDispatchIntervalSeconds,
    string PublicKey,
    string SecretKey,
    string TestPublicKey,
    string TestSecretKey,
    string TestWebhookSecret,
    string ProductionPublicKey,
    string ProductionSecretKey,
    string ProductionWebhookSecret);

public class RyvePoolException : Exception
{
    public string ErrorCode { get; }
    public int HttpStatusCode { get; }
    public string? RawResponse { get; }

    public RyvePoolException(string errorCode, string message, int httpStatusCode = 0, string? rawResponse = null)
        : base(message)
    {
        ErrorCode = errorCode;
        HttpStatusCode = httpStatusCode;
        RawResponse = rawResponse;
    }
}
