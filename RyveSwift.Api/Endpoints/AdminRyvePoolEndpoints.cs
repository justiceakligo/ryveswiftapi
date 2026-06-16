using Microsoft.EntityFrameworkCore;
using RyveSwift.Api.Common;
using RyveSwift.Api.Data;
using RyveSwift.Api.Dtos;
using RyveSwift.Api.Entities;
using RyveSwift.Api.Services;

namespace RyveSwift.Api.Endpoints;

public static class AdminRyvePoolEndpoints
{
    public static void MapAdminRyvePoolEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/admin/ryvepool")
            .WithTags("Admin RyvePool")
            .RequireAuthorization("AdminOnly");

        group.MapGet("/config", GetConfig)
            .WithName("AdminGetRyvePoolConfig")
            .WithSummary("Get RyvePool order delivery configuration status");

        group.MapPut("/config", UpdateConfig)
            .WithName("AdminUpdateRyvePoolConfig")
            .WithSummary("Update RyvePool test/production configuration");

        group.MapPost("/test-quote", TestQuote)
            .WithName("AdminTestRyvePoolQuote")
            .WithSummary("Send a test quote request to the active RyvePool environment");

        group.MapPost("/webhooks/test", SendWebhookTest)
            .WithName("AdminSendRyvePoolWebhookTest")
            .WithSummary("Ask RyvePool to send a webhook test ping");

        group.MapGet("/webhook-events", GetRemoteWebhookEvents)
            .WithName("AdminGetRyvePoolWebhookEvents")
            .WithSummary("Get RyvePool webhook delivery logs from the active environment");

        group.MapGet("/reports/summary", GetReportSummary)
            .WithName("AdminGetRyvePoolReportSummary")
            .WithSummary("Get RyvePool reporting summary from the active environment");

        group.MapGet("/reports/branches", GetBranchReport)
            .WithName("AdminGetRyvePoolBranchReport")
            .WithSummary("Get RyvePool branch-level report from the active environment");

        group.MapGet("/deliveries", GetDeliveries)
            .WithName("AdminGetRyvePoolDeliveries")
            .WithSummary("List locally stored RyvePool order deliveries");

        group.MapGet("/deliveries/{id:guid}", GetDelivery)
            .WithName("AdminGetRyvePoolDelivery")
            .WithSummary("Get a locally stored RyvePool order delivery");
    }

    private static IResult GetConfig(RyvePoolService ryvePool) =>
        Results.Ok(BuildConfigResponse(ryvePool.GetRuntimeConfig()));

    private static async Task<IResult> UpdateConfig(
        RyvePoolConfigUpdateRequest req,
        ConfigService config,
        RyvePoolService ryvePool)
    {
        try
        {
            if (req.Enabled.HasValue)
                await config.SetAsync("RYVEPOOL_ENABLED", req.Enabled.Value ? "true" : "false");

            if (!string.IsNullOrWhiteSpace(req.Environment))
                await config.SetAsync("RYVEPOOL_ENVIRONMENT", RyvePoolService.NormalizeEnvironment(req.Environment));

            if (!string.IsNullOrWhiteSpace(req.BaseUrl))
                await config.SetAsync("RYVEPOOL_BASE_URL", req.BaseUrl.Trim().TrimEnd('/'));

            if (req.TimeoutSeconds.HasValue)
                await config.SetAsync("RYVEPOOL_TIMEOUT_SECONDS", Math.Clamp(req.TimeoutSeconds.Value, 5, 120).ToString());

            if (req.DefaultRegionCode is not null)
                await config.SetAsync("RYVEPOOL_DEFAULT_REGION_CODE", req.DefaultRegionCode.Trim().ToUpperInvariant());

            if (req.DefaultExternalBranchId is not null)
                await config.SetAsync("RYVEPOOL_DEFAULT_EXTERNAL_BRANCH_ID", req.DefaultExternalBranchId.Trim());

            if (req.DefaultDispatchMode is not null)
                await config.SetAsync("RYVEPOOL_DEFAULT_DISPATCH_MODE", RyvePoolService.NormalizeDispatchMode(req.DefaultDispatchMode));

            if (req.DefaultPackageType is not null)
                await config.SetAsync("RYVEPOOL_DEFAULT_PACKAGE_TYPE", RyvePoolService.NormalizePackageType(req.DefaultPackageType));

            if (req.WebhookSignatureRequired.HasValue)
                await config.SetAsync("RYVEPOOL_WEBHOOK_SIGNATURE_REQUIRED", req.WebhookSignatureRequired.Value ? "true" : "false");

            if (req.ScheduledDispatchEnabled.HasValue)
                await config.SetAsync("RYVEPOOL_SCHEDULED_DISPATCH_ENABLED", req.ScheduledDispatchEnabled.Value ? "true" : "false");

            if (req.ScheduledDispatchIntervalSeconds.HasValue)
                await config.SetAsync("RYVEPOOL_SCHEDULED_DISPATCH_INTERVAL_SECONDS", Math.Clamp(req.ScheduledDispatchIntervalSeconds.Value, 15, 3600).ToString());

            await SetIfProvided(config, "RYVEPOOL_TEST_PUBLIC_KEY", req.TestPublicKey);
            await SetIfProvided(config, "RYVEPOOL_TEST_SECRET_KEY", req.TestSecretKey);
            await SetIfProvided(config, "RYVEPOOL_TEST_WEBHOOK_SECRET", req.TestWebhookSecret);
            await SetIfProvided(config, "RYVEPOOL_PRODUCTION_PUBLIC_KEY", req.ProductionPublicKey);
            await SetIfProvided(config, "RYVEPOOL_PRODUCTION_SECRET_KEY", req.ProductionSecretKey);
            await SetIfProvided(config, "RYVEPOOL_PRODUCTION_WEBHOOK_SECRET", req.ProductionWebhookSecret);

            return Results.Ok(BuildConfigResponse(ryvePool.GetRuntimeConfig()));
        }
        catch (RyvePoolException ex)
        {
            return Results.Json(new ApiError(ex.ErrorCode, ex.Message), statusCode: ex.HttpStatusCode == 0 ? 422 : ex.HttpStatusCode);
        }
    }

    private static async Task<IResult> TestQuote(
        RyvePoolQuoteRequest req,
        RyvePoolService ryvePool,
        CancellationToken ct)
    {
        try
        {
            var cfg = ryvePool.GetRuntimeConfig();
            var quote = await ryvePool.GetQuoteAsync(new RyvePoolQuoteApiRequest
            {
                PickupLat = req.PickupLat,
                PickupLng = req.PickupLng,
                DropoffLat = req.DropoffLat,
                DropoffLng = req.DropoffLng,
                PickupCity = req.PickupCity,
                DropoffCity = req.DropoffCity,
                PackageType = RyvePoolService.NormalizePackageType(req.PackageType ?? cfg.DefaultPackageType),
                WeightKg = req.WeightKg,
                RegionCode = string.IsNullOrWhiteSpace(req.RegionCode) ? cfg.DefaultRegionCode : req.RegionCode.Trim(),
                VehicleCategoryId = req.VehicleCategoryId
            }, ct);
            return Results.Json(quote.Json);
        }
        catch (RyvePoolException ex)
        {
            return RyvePoolError(ex);
        }
    }

    private static async Task<IResult> SendWebhookTest(RyvePoolService ryvePool, CancellationToken ct)
    {
        try
        {
            var result = await ryvePool.SendWebhookTestAsync(ct);
            return Results.Json(result.Json);
        }
        catch (RyvePoolException ex)
        {
            return RyvePoolError(ex);
        }
    }

    private static async Task<IResult> GetRemoteWebhookEvents(
        RyvePoolService ryvePool,
        string? eventType = null,
        string? status = null,
        int limit = 50,
        CancellationToken ct = default)
    {
        try
        {
            var result = await ryvePool.GetWebhookEventsAsync(eventType, status, limit, ct);
            return Results.Json(result.Json);
        }
        catch (RyvePoolException ex)
        {
            return RyvePoolError(ex);
        }
    }

    private static async Task<IResult> GetReportSummary(
        RyvePoolService ryvePool,
        DateTime? from = null,
        DateTime? to = null,
        string? branchId = null,
        string? regionCode = null,
        CancellationToken ct = default)
    {
        try
        {
            var result = await ryvePool.GetReportSummaryAsync(from, to, branchId, regionCode, ct);
            return Results.Json(result.Json);
        }
        catch (RyvePoolException ex)
        {
            return RyvePoolError(ex);
        }
    }

    private static async Task<IResult> GetBranchReport(
        RyvePoolService ryvePool,
        DateTime? from = null,
        DateTime? to = null,
        string? branchId = null,
        string? regionCode = null,
        CancellationToken ct = default)
    {
        try
        {
            var result = await ryvePool.GetBranchReportAsync(from, to, branchId, regionCode, ct);
            return Results.Json(result.Json);
        }
        catch (RyvePoolException ex)
        {
            return RyvePoolError(ex);
        }
    }

    private static async Task<IResult> GetDeliveries(
        AppDbContext db,
        int page = 1,
        int pageSize = 50,
        string? environment = null,
        string? status = null)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var query = db.RyvePoolDeliveries
            .Include(d => d.User)
            .AsNoTracking()
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(environment))
        {
            var normalized = RyvePoolService.NormalizeEnvironment(environment);
            query = query.Where(d => d.Environment == normalized);
        }

        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(d => d.Status == status);

        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(d => d.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(d => new
            {
                d.Id,
                d.QuoteId,
                d.ShipmentId,
                d.Environment,
                d.ExternalOrderId,
                d.RyvePoolDispatchId,
                d.Status,
                d.TrackingUrl,
                d.RegionCode,
                d.ExternalBranchId,
                d.DispatchModeUsed,
                d.PaymentType,
                d.CodAmountMinor,
                d.PackageType,
                d.Currency,
                d.DeliveryFeeMinor,
                d.PlatformFeeMinor,
                d.RyvePoolCommissionMinor,
                d.DriverPayoutMinor,
                d.CanCancel,
                d.CancellableUntil,
                d.DispatchTiming,
                d.ScheduledForUtc,
                d.DispatchAttemptCount,
                d.LastDispatchAttemptAt,
                d.LastDispatchError,
                d.DhlPointId,
                d.DhlPointName,
                d.CreatedAt,
                d.UpdatedAt,
                UserEmail = d.User != null ? d.User.Email : null
            })
            .ToListAsync();

        return Results.Ok(new PaginatedResult<object>(items.Cast<object>().ToList(), total, page, pageSize));
    }

    private static async Task<IResult> GetDelivery(Guid id, AppDbContext db)
    {
        var delivery = await db.RyvePoolDeliveries
            .Include(d => d.User)
            .Include(d => d.WebhookEvents.OrderByDescending(e => e.ReceivedAt).Take(20))
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == id);

        if (delivery is null)
            return Results.NotFound(new ApiError("not_found", "RyvePool order delivery not found."));

        return Results.Ok(new
        {
            delivery.Id,
            delivery.QuoteId,
            delivery.ShipmentId,
            delivery.Environment,
            delivery.ExternalOrderId,
            delivery.MerchantReference,
            delivery.ExternalBranchId,
            delivery.RyvePoolDispatchId,
            delivery.Status,
            delivery.TrackingUrl,
            delivery.RegionCode,
            delivery.Timezone,
            delivery.DispatchModeRequested,
            delivery.DispatchModeUsed,
            delivery.DriverPool,
            delivery.PaymentType,
            delivery.CodAmountMinor,
            delivery.PackageType,
            delivery.ParcelWeightKg,
            delivery.DriverInstructions,
            delivery.Currency,
            delivery.DeliveryFeeMinor,
            delivery.PlatformFeeMinor,
            delivery.RyvePoolCommissionMinor,
            delivery.DriverPayoutMinor,
            delivery.NotificationFeeMinor,
            delivery.TaxMinor,
            delivery.PaymentProcessingFeeMinor,
            delivery.RefundAdjustmentMinor,
            delivery.SettlementStatus,
            delivery.CancellationWindowMinutes,
            delivery.CancellableUntil,
            delivery.CanCancel,
            delivery.ShortCode,
            delivery.DispatchTiming,
            delivery.ScheduledForUtc,
            delivery.DispatchAttemptCount,
            delivery.LastDispatchAttemptAt,
            delivery.LastDispatchError,
            delivery.DhlPointId,
            delivery.DhlPointName,
            pickup = new
            {
                delivery.PickupName,
                delivery.PickupPhone,
                delivery.PickupAddress,
                delivery.PickupLandmark,
                delivery.PickupLat,
                delivery.PickupLng
            },
            dropoff = new
            {
                delivery.DropoffName,
                delivery.DropoffPhone,
                delivery.DropoffEmail,
                delivery.DropoffAddress,
                delivery.DropoffLandmark,
                delivery.DropoffLat,
                delivery.DropoffLng
            },
            delivery.MetadataJson,
            delivery.RawQuoteResponse,
            delivery.RawCreateResponse,
            delivery.RawLatestResponse,
            delivery.CreatedAt,
            delivery.UpdatedAt,
            delivery.PickedUpAt,
            delivery.DeliveredAt,
            delivery.CancelledAt,
            delivery.FailedAt,
            user = delivery.User is null ? null : new { delivery.User.Id, delivery.User.Email, delivery.User.FullName },
            webhookEvents = delivery.WebhookEvents.Select(e => new
            {
                e.Id,
                e.RyvePoolEventId,
                e.Event,
                e.Environment,
                e.DispatchId,
                e.ExternalOrderId,
                e.PreviousStatus,
                e.Status,
                e.IsSignatureValid,
                e.ReceivedAt
            })
        });
    }

    private static RyvePoolConfigResponse BuildConfigResponse(RyvePoolRuntimeConfig cfg) => new(
        cfg.Enabled,
        cfg.Environment,
        cfg.BaseUrl,
        cfg.TimeoutSeconds,
        cfg.DefaultRegionCode,
        cfg.DefaultExternalBranchId,
        cfg.DefaultDispatchMode,
        cfg.DefaultPackageType,
        cfg.WebhookSignatureRequired,
        cfg.ScheduledDispatchEnabled,
        cfg.ScheduledDispatchIntervalSeconds,
        new RyvePoolCredentialStatus(
            MaskPublicKey(cfg.TestPublicKey),
            !RyvePoolService.IsPlaceholder(cfg.TestSecretKey),
            !RyvePoolService.IsPlaceholder(cfg.TestWebhookSecret)),
        new RyvePoolCredentialStatus(
            MaskPublicKey(cfg.ProductionPublicKey),
            !RyvePoolService.IsPlaceholder(cfg.ProductionSecretKey),
            !RyvePoolService.IsPlaceholder(cfg.ProductionWebhookSecret)));

    private static async Task SetIfProvided(ConfigService config, string key, string? value)
    {
        if (value is not null)
            await config.SetAsync(key, value.Trim());
    }

    private static string? MaskPublicKey(string? key)
    {
        if (RyvePoolService.IsPlaceholder(key))
            return null;

        key = key!.Trim();
        if (key.Length <= 12)
            return key;

        return $"{key[..8]}...{key[^4..]}";
    }

    private static IResult RyvePoolError(RyvePoolException ex)
    {
        var statusCode = ex.HttpStatusCode == 0 ? StatusCodes.Status503ServiceUnavailable : ex.HttpStatusCode;
        return Results.Json(new ApiError(ex.ErrorCode, ex.Message), statusCode: statusCode);
    }
}
