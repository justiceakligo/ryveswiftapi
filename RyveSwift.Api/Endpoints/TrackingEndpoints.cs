using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RyveSwift.Api.Common;
using RyveSwift.Api.Data;
using RyveSwift.Api.Dtos;
using RyveSwift.Api.Entities;
using RyveSwift.Api.Services;

namespace RyveSwift.Api.Endpoints;

public static class TrackingEndpoints
{
    private static readonly HashSet<string> ActiveStatuses = new(StringComparer.OrdinalIgnoreCase)
        { "Booked", "LabelGenerated", "DroppedOff", "InTransit" };

    public static void MapTrackingEndpoints(this WebApplication app)
    {
        // Public — no auth required
        app.MapGet("/api/track/{trackingNumber}", TrackShipment)
            .WithTags("Tracking")
            .WithName("TrackShipment")
            .WithSummary("Get live tracking information from DHL (public)")
            .AllowAnonymous();

        // Admin job
        app.MapPost("/api/admin/sync-tracking", SyncTracking)
            .WithTags("Admin")
            .WithName("SyncTracking")
            .WithSummary("Manually trigger tracking sync for all active shipments")
            .RequireAuthorization("AdminOnly");
    }

    private static async Task<IResult> TrackShipment(
        string trackingNumber, AppDbContext db, DhlService dhl)
    {
        try
        {
            var dhlResponse = await dhl.GetTrackingAsync(trackingNumber);

            var shipmentTracking = dhlResponse.Shipments.FirstOrDefault();
            if (shipmentTracking is null)
                return Results.NotFound(new ApiError("not_found", "No tracking information found for this number."));

            var events = shipmentTracking.Events.Select(e => new TrackingEventResponse(
                ParseDhlTimestamp(e.Timestamp),
                e.Location?.Address?.AddressLocality ?? e.Location?.Address?.CountryCode,
                e.Description
            )).ToList();

            var status = shipmentTracking.Status?.Status ?? "Unknown";

            // Estimated delivery from DHL (if available)
            var estimatedDelivery = shipmentTracking.EstimatedDeliveryDate;

            // Persist events to DB if we find the shipment
            var dbShipment = await db.Shipments
                .FirstOrDefaultAsync(s => s.TrackingNumber == trackingNumber);

            if (dbShipment is not null)
            {
                foreach (var e in shipmentTracking.Events)
                {
                    var eventDesc = e.Description ?? "Tracking update";
                    var exists = await db.ShipmentEvents.AnyAsync(
                        se => se.ShipmentId == dbShipment.Id && se.Description == eventDesc);

                    if (!exists)
                    {
                        db.ShipmentEvents.Add(new ShipmentEvent
                        {
                            ShipmentId = dbShipment.Id,
                            EventType = "TrackingUpdate",
                            Description = eventDesc,
                            Location = e.Location?.Address?.AddressLocality,
                            RawPayload = JsonSerializer.Serialize(e)
                        });
                    }
                }

                // Update shipment status based on DHL tracking
                var newStatus = MapDhlStatusToInternal(status);
                if (newStatus is not null && dbShipment.Status != newStatus)
                {
                    dbShipment.Status = newStatus;
                    dbShipment.UpdatedAt = DateTime.UtcNow;
                }

                await db.SaveChangesAsync();
            }

            return Results.Ok(new TrackingResponse(trackingNumber, status, estimatedDelivery, events));
        }
        catch (DhlException ex)
        {
            return Results.BadRequest(new ApiError(ex.ErrorCode, ex.Message));
        }
    }

    private static async Task<IResult> SyncTracking(AppDbContext db, DhlService dhl, ILogger<Program> logger)
    {
        var activeShipments = await db.Shipments
            .Where(s => ActiveStatuses.Contains(s.Status) && s.TrackingNumber != null)
            .ToListAsync();

        int updated = 0;
        foreach (var shipment in activeShipments)
        {
            try
            {
                var dhlResponse = await dhl.GetTrackingAsync(shipment.TrackingNumber!);
                var tracking = dhlResponse.Shipments.FirstOrDefault();
                if (tracking is null) continue;

                var newStatus = MapDhlStatusToInternal(tracking.Status?.Status ?? "");
                if (newStatus is not null && shipment.Status != newStatus)
                {
                    shipment.Status = newStatus;
                    shipment.UpdatedAt = DateTime.UtcNow;
                    updated++;
                }

                foreach (var e in tracking.Events)
                {
                    var eventDesc = e.Description ?? "Tracking update";
                    var exists = await db.ShipmentEvents.AnyAsync(
                        se => se.ShipmentId == shipment.Id && se.Description == eventDesc);
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
                logger.LogWarning("Tracking sync failed for {TrackingNumber}: {Error}",
                    shipment.TrackingNumber, ex.Message);
            }
        }

        await db.SaveChangesAsync();
        return Results.Ok(new { message = $"Sync complete. {activeShipments.Count} checked, {updated} statuses updated." });
    }

    private static string? MapDhlStatusToInternal(string dhlStatus)
    {
        return dhlStatus.ToUpper() switch
        {
            "TRANSIT" => "InTransit",
            "DELIVERED" => "Delivered",
            "DELIVERY_FAILURE" => "Exception",
            "DELIVERY_IMPOSSIBLE" => "Exception",
            _ => null
        };
    }

    private static DateTime ParseDhlTimestamp(string? timestamp)
    {
        if (string.IsNullOrWhiteSpace(timestamp)) return DateTime.UtcNow;
        if (DateTime.TryParse(timestamp, out var dt)) return dt.ToUniversalTime();
        return DateTime.UtcNow;
    }
}
