using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RyveSwift.Api.Data;
using RyveSwift.Api.Dhl;
using RyveSwift.Api.Entities;

namespace RyveSwift.Api.Services;

public class TrackingPollingService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TrackingPollingService> _logger;

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
        var emails = scope.ServiceProvider.GetRequiredService<NotificationEmailService>();

        var activeShipments = await db.Shipments
            .Where(s => s.TrackingNumber != null &&
                (s.Status == "Booked" ||
                 s.Status == "LabelGenerated" ||
                 s.Status == "DroppedOff" ||
                 s.Status == "InTransit"))
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

                var newStatus = MapDhlStatus(GetDhlStatusCode(tracking.Status));
                if (newStatus is not null && shipment.Status != newStatus)
                {
                    var oldStatus = shipment.Status;
                    _logger.LogInformation("Shipment {Id} status updated: {Old} -> {New}",
                        shipment.Id, oldStatus, newStatus);
                    shipment.Status = newStatus;
                    shipment.UpdatedAt = DateTime.UtcNow;
                    updated++;

                    var user = shipment.UserId.HasValue
                        ? await db.Users.FindAsync(new object?[] { shipment.UserId.Value }, ct)
                        : null;
                    await emails.SendShipmentStatusChangedAsync(shipment, user, oldStatus, newStatus, ct);
                }

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

            await Task.Delay(500, ct);
        }

        await db.SaveChangesAsync(ct);
        _logger.LogInformation("Tracking sync complete. {Updated}/{Total} statuses updated.", updated, activeShipments.Count);
    }

    private static string? MapDhlStatus(string? dhlStatus) => dhlStatus?.Trim().ToUpperInvariant() switch
    {
        "PICKED-UP" or "PU" => "DroppedOff",
        "TRANSIT" => "InTransit",
        "OUT-FOR-DELIVERY" => "OutForDelivery",
        "DELIVERED" or "OK" => "Delivered",
        "FAILURE" or "RT" or "DELIVERY_FAILURE" or "DELIVERY_IMPOSSIBLE" => "Exception",
        _ => null
    };

    private static string? GetDhlStatusCode(DhlTrackingStatus? status) =>
        status?.StatusCode ?? status?.Status ?? status?.Description;
}
