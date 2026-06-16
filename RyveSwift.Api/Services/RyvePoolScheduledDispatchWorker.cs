using Microsoft.EntityFrameworkCore;
using RyveSwift.Api.Data;

namespace RyveSwift.Api.Services;

public class RyvePoolScheduledDispatchWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<RyvePoolScheduledDispatchWorker> _logger;

    public RyvePoolScheduledDispatchWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<RyvePoolScheduledDispatchWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("RyvePoolScheduledDispatchWorker started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            var delaySeconds = 60;

            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var config = scope.ServiceProvider.GetRequiredService<ConfigService>();
                delaySeconds = Math.Clamp(config.GetInt("RYVEPOOL_SCHEDULED_DISPATCH_INTERVAL_SECONDS", 60), 15, 3600);

                if (GetBool(config, "RYVEPOOL_SCHEDULED_DISPATCH_ENABLED", true))
                    await DispatchDueDeliveriesAsync(scope.ServiceProvider, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in RyvePoolScheduledDispatchWorker");
            }

            await Task.Delay(TimeSpan.FromSeconds(delaySeconds), stoppingToken);
        }
    }

    private async Task DispatchDueDeliveriesAsync(IServiceProvider services, CancellationToken ct)
    {
        var db = services.GetRequiredService<AppDbContext>();
        var ryvePool = services.GetRequiredService<RyvePoolService>();
        var coordinator = services.GetRequiredService<RyvePoolDispatchCoordinator>();
        var cfg = ryvePool.GetRuntimeConfig();

        if (!cfg.Enabled)
            return;

        var now = DateTime.UtcNow;
        var dueDeliveries = await db.RyvePoolDeliveries
            .Where(d =>
                d.Environment == cfg.Environment &&
                d.RyvePoolDispatchId == null &&
                d.Status == "scheduled" &&
                d.ScheduledForUtc != null &&
                d.ScheduledForUtc <= now)
            .OrderBy(d => d.ScheduledForUtc)
            .Take(25)
            .ToListAsync(ct);

        if (dueDeliveries.Count == 0)
            return;

        _logger.LogInformation("Dispatching {Count} due RyvePool deliveries.", dueDeliveries.Count);

        foreach (var delivery in dueDeliveries)
        {
            if (ct.IsCancellationRequested)
                break;

            await coordinator.TryDispatchStoredDeliveryAsync(delivery, ct);
        }
    }

    private static bool GetBool(ConfigService config, string key, bool defaultValue)
    {
        var value = config.Get(key, defaultValue ? "true" : "false");
        return bool.TryParse(value, out var parsed) ? parsed : defaultValue;
    }
}
