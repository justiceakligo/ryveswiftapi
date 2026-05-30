using Microsoft.EntityFrameworkCore;
using RyveSwift.Api.Data;
using RyveSwift.Api.Entities;
using RyveSwift.Api.Services;
using System.Text.Json;

namespace RyveSwift.Api.Services;

public class TrackingPollingService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TrackingPollingService> _logger;

    private static readonly HashSet<string> ActiveStatuses = new(StringComparer.OrdinalIgnoreCase)
        { "Booked", "LabelGenerated", "DroppedOff", "InTransit" };

    public TrackingPollingService(IServiceScopeFactory scopeFactory, ILogger<TrackingPollingService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("TrackingPollingService started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SyncTrackingAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in TrackingPollingService");
            }

            await using var scope = _scopeFactory.CreateAsyncScope();
            var config = scope.ServiceProvider.GetRequiredService<ConfigService>();
            var intervalMinutes = config.GetInt("TRACKING_POLL_INTERVAL_MINUTES", 30);

            _logger.LogInformation("TrackingPollingService sleeping for {Minutes} minutes.", intervalMinutes);
            await Task.Delay(TimeSpan.FromMinutes(intervalMinutes), stoppingToken);
        }
    }

    private async Task SyncTrackingAsync(CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var dhl = scope.ServiceProvider.GetRequiredService<DhlService>();

        var activeShipments = await db.Shipments
            .Where(s => ActiveStatuses.Contains(s.Status) && s.TrackingNumber != null)
            .ToListAsync(ct);

        if (activeShipments.Count == 0)
        {
            _logger.LogDebug("No active shipments to track.");
            return;
        }

        _logger.LogInformation("Syncing tracking for {Count} active shipments.", activeShipments.Count);
        int updated = 0;

        foreach (var shipment in activeShipments)
        {
            if (ct.IsCancellationRequested) break;

            try
            {
                var dhlResponse = await dhl.GetTrackingAsync(shipment.TrackingNumber!);
                var tracking = dhlResponse.Shipments.FirstOrDefault();
                if (tracking is null) continue;

                // Update status
                var newStatus = MapDhlStatus(tracking.Status?.Status ?? "");
                if (newStatus is not null && shipment.Status != newStatus)
                {
                    _logger.LogInformation("Shipment {Id} status updated: {Old} → {New}",
                        shipment.Id, shipment.Status, newStatus);
                    shipment.Status = newStatus;
                    shipment.UpdatedAt = DateTime.UtcNow;
                    updated++;
                }

                // Persist new events
                foreach (var e in tracking.Events)
                {
                    var eventDesc = e.Description ?? "Tracking update";
                    var exists = await db.ShipmentEvents.AnyAsync(
                        se => se.ShipmentId == shipment.Id && se.Description == eventDesc, ct);

                    if (!exists)
                    {
                        db.ShipmentEvents.Add(new ShipmentEvent
                        {
                            ShipmentId = shipment.Id,
                            EventType = "TrackingUpdate",
                            Description = eventDesc,
                            Location = e.Location?.Address?.AddressLocality,
                            RawPayload = JsonSerializer.Serialize(e)
                        });
                    }
                }
            }
            catch (DhlException ex)
            {
                _logger.LogWarning("Tracking failed for {TrackingNumber}: {Error}",
                    shipment.TrackingNumber, ex.Message);
            }

            await Task.Delay(500, ct); // polite delay between DHL calls
        }

        await db.SaveChangesAsync(ct);
        _logger.LogInformation("Tracking sync complete. {Updated}/{Total} statuses updated.", updated, activeShipments.Count);
    }

    private static string? MapDhlStatus(string dhlStatus) => dhlStatus.ToUpper() switch
    {
        "TRANSIT" => "InTransit",
        "DELIVERED" => "Delivered",
        "DELIVERY_FAILURE" or "DELIVERY_IMPOSSIBLE" => "Exception",
        _ => null
    };
}
