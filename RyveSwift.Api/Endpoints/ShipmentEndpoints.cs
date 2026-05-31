using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using RyveSwift.Api.Common;
using RyveSwift.Api.Data;
using RyveSwift.Api.Dtos;
using RyveSwift.Api.Entities;
using RyveSwift.Api.Services;

namespace RyveSwift.Api.Endpoints;

public static class ShipmentEndpoints
{
    private static readonly HashSet<string> VagueDescriptions = new(StringComparer.OrdinalIgnoreCase)
        { "ANY", "GOODS", "ITEMS", "SAMPLES", "STUFF", "THINGS", "MISC", "MISCELLANEOUS", "PACKAGE", "PACKAGES" };

    private static readonly HashSet<string> InvalidHsCodes = new(StringComparer.OrdinalIgnoreCase)
        { "999999", "000000" };

    public static void MapShipmentEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/shipments").WithTags("Shipments").RequireAuthorization();

        group.MapPost("/from-quote", CreateFromQuote)
            .WithName("CreateShipmentFromQuote")
            .WithSummary("[Legacy] Create a shipment from an accepted quote (use /api/bookings/confirm instead)");

        group.MapGet("/{id:guid}", GetShipment)
            .WithName("GetShipment")
            .WithSummary("Get shipment details");

        group.MapGet("", GetMyShipments)
            .WithName("GetMyShipments")
            .WithSummary("Get all shipments for the current user");

        group.MapPost("/{id:guid}/create-label", CreateLabel)
            .WithName("CreateLabel")
            .WithSummary("Manually trigger DHL shipment and label creation (admin or retry)");

        group.MapPost("/{id:guid}/cancel", CancelShipment)
            .WithName("CancelShipment")
            .WithSummary("Cancel a shipment that has not been booked");

        // Unified documents endpoint: GET /api/shipments/:id/documents/:type
        group.MapGet("/{id:guid}/documents/{type}", DownloadDocument)
            .WithName("DownloadDocument")
            .WithSummary("Download a shipment document (label | invoice | waybill)");

        // Legacy per-type endpoints kept for backward compat
        group.MapGet("/{id:guid}/label",   (Guid id, HttpContext ctx, AppDbContext db, SpacesStorageService spaces) => DownloadFile(id, ctx, db, spaces, s => s.LabelFilePath,   "label.pdf"));
        group.MapGet("/{id:guid}/invoice", (Guid id, HttpContext ctx, AppDbContext db, SpacesStorageService spaces) => DownloadFile(id, ctx, db, spaces, s => s.InvoiceFilePath, "invoice.pdf"));
        group.MapGet("/{id:guid}/waybill", (Guid id, HttpContext ctx, AppDbContext db, SpacesStorageService spaces) => DownloadFile(id, ctx, db, spaces, s => s.WaybillFilePath, "waybill.pdf"));
    }

    private static async Task<IResult> CreateFromQuote(
        CreateShipmentFromQuoteRequest req, HttpContext ctx, AppDbContext db)
    {
        var userId = GetUserId(ctx);

        // Validate quote
        var quote = await db.Quotes.FirstOrDefaultAsync(q => q.Id == req.QuoteId && q.UserId == userId);
        if (quote is null)
            return Results.NotFound(new ApiError("not_found", "Quote not found."));
        if (quote.ExpiresAt < DateTime.UtcNow)
            return Results.BadRequest(new ApiError("quote_expired", "This quote has expired. Please request a new quote."));

        // Validate addresses
        var senderAddress = await db.Addresses.FirstOrDefaultAsync(a => a.Id == req.SenderAddressId && a.UserId == userId);
        if (senderAddress is null)
            return Results.NotFound(new ApiError("not_found", "Sender address not found."));

        var receiverAddress = await db.Addresses.FirstOrDefaultAsync(a => a.Id == req.ReceiverAddressId && a.UserId == userId);
        if (receiverAddress is null)
            return Results.NotFound(new ApiError("not_found", "Receiver address not found."));

        // Validate customs items (required for non-documents)
        if (!quote.ProductCode.Equals("D", StringComparison.OrdinalIgnoreCase))
        {
            if (req.CustomsItems == null || req.CustomsItems.Count == 0)
                return Results.BadRequest(new ApiError("invalid_customs_data", "Customs items are required for parcel shipments."));

            foreach (var item in req.CustomsItems)
            {
                if (VagueDescriptions.Contains(item.Description.Trim()))
                    return Results.BadRequest(new ApiError("invalid_customs_data",
                        $"Description '{item.Description}' is not specific enough. Provide the actual product name."));

                if (item.Description.Trim().Length < 3)
                    return Results.BadRequest(new ApiError("invalid_customs_data", "Customs item description must be at least 3 characters."));

                if (!string.IsNullOrWhiteSpace(item.HsCode))
                {
                    var hs = item.HsCode.Trim();
                    if (InvalidHsCodes.Contains(hs) || hs.Length != 6 || !hs.All(char.IsDigit))
                        return Results.BadRequest(new ApiError("invalid_customs_data",
                            $"HS code '{item.HsCode}' is invalid. Provide a real 6-digit HS code."));
                }

                if (item.Quantity <= 0 || item.UnitPrice <= 0)
                    return Results.BadRequest(new ApiError("invalid_customs_data", "Customs item quantity and unit price must be greater than 0."));
            }

            // Cross-check declared weight vs customs gross weights (only when all items have weight set)
            if (req.CustomsItems.All(i => i.GrossWeightKg.HasValue))
            {
                var totalCustomsWeight = req.CustomsItems.Sum(i => i.GrossWeightKg!.Value);
                var packageWeight = quote.WeightKg;
                var variance = Math.Abs(totalCustomsWeight - packageWeight) / packageWeight;
                if (variance > 0.25m)
                    return Results.BadRequest(new ApiError("invalid_customs_data",
                        $"Total customs gross weight ({totalCustomsWeight:F2} kg) does not match package weight ({packageWeight:F2} kg)."));
            }
        }

        var shipment = new Shipment
        {
            UserId = userId,
            QuoteId = quote.Id,
            SenderAddressId = senderAddress.Id,
            ReceiverAddressId = receiverAddress.Id,
            ProductCode = quote.ProductCode,
            OriginCountry = quote.OriginCountry,
            DestinationCountry = quote.DestinationCountry,
            Status = "PendingPayment",
            DhlBaseRate = quote.DhlBaseRate,
            MarkupPercent = quote.MarkupPercent,
            PlatformFee = quote.PlatformFee,
            TotalAmount = quote.TotalAmount,
            Currency = quote.Currency,
            ExportReason = req.ExportReason?.Trim(),
            InvoiceNumber = req.InvoiceNumber?.Trim(),
            InvoiceDate = req.InvoiceDate
        };

        db.Shipments.Add(shipment);

        // Add package from quote
        db.ShipmentPackages.Add(new ShipmentPackage
        {
            ShipmentId = shipment.Id,
            WeightKg = quote.WeightKg,
            LengthCm = quote.LengthCm,
            WidthCm = quote.WidthCm,
            HeightCm = quote.HeightCm
        });

        // Add customs items
        if (req.CustomsItems != null)
        {
            foreach (var item in req.CustomsItems)
            {
                db.CustomsItems.Add(new CustomsItem
                {
                    ShipmentId          = shipment.Id,
                    Description         = item.Description.Trim(),
                    Quantity            = item.Quantity,
                    UnitOfMeasurement   = item.UnitOfMeasurement?.Trim() ?? "PCS",
                    UnitPrice           = item.UnitPrice,
                    Currency            = item.Currency?.ToUpper() ?? "USD",
                    HsCode              = item.HsCode?.Trim() ?? "",
                    ManufacturerCountry = item.ManufacturerCountry?.ToUpper() ?? "",
                    NetWeightKg         = item.NetWeightKg ?? 0,
                    GrossWeightKg       = item.GrossWeightKg ?? 0
                });
            }
        }

        db.ShipmentEvents.Add(new ShipmentEvent
        {
            ShipmentId = shipment.Id,
            EventType = "ShipmentCreated",
            Description = "Shipment created from quote, pending payment."
        });

        await db.SaveChangesAsync();

        return Results.Created($"/api/shipments/{shipment.Id}", MapToSummaryResponse(shipment));
    }

    private static async Task<IResult> GetShipment(Guid id, HttpContext ctx, AppDbContext db)
    {
        var userId = GetUserId(ctx);
        var isAdmin = ctx.User.IsInRole("Admin");

        var shipment = await db.Shipments
            .Include(s => s.SenderAddress)
            .Include(s => s.ReceiverAddress)
            .Include(s => s.Packages)
            .Include(s => s.CustomsItems)
            .Include(s => s.Payments)
            .FirstOrDefaultAsync(s => s.Id == id && (isAdmin || s.UserId == userId));

        if (shipment is null)
            return Results.NotFound(new ApiError("not_found", "Shipment not found."));

        return Results.Ok(MapToDetailResponse(shipment));
    }

    private static async Task<IResult> GetMyShipments(HttpContext ctx, AppDbContext db)
    {
        var userId = GetUserId(ctx);
        var shipments = await db.Shipments
            .Where(s => s.UserId == userId)
            .Include(s => s.Packages)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync();

        return Results.Ok(new ShipmentListResponse(
            shipments.Select(s => MapToListItem(s)).ToList()));
    }

    private static async Task<IResult> CreateLabel(
        Guid id,
        HttpContext ctx,
        AppDbContext db,
        DhlService dhl,
        SpacesStorageService spaces,
        ILogger<Program> logger,
        NotificationEmailService emails)
    {
        var isAdmin = ctx.User.IsInRole("Admin");
        var userId = GetUserId(ctx);

        var shipment = await db.Shipments
            .Include(s => s.SenderAddress)
            .Include(s => s.ReceiverAddress)
            .Include(s => s.Packages)
            .Include(s => s.CustomsItems)
            .FirstOrDefaultAsync(s => s.Id == id && (isAdmin || s.UserId == userId));

        if (shipment is null)
            return Results.NotFound(new ApiError("not_found", "Shipment not found."));

        if (shipment.Status != "PaymentAuthorized" && shipment.Status != "Booked")
            return Results.BadRequest(new ApiError("validation_failed",
                "Shipment must be in PaymentAuthorized or Booked status to create a label."));

        if (shipment.SenderAddress is null || shipment.ReceiverAddress is null)
            return Results.BadRequest(new ApiError("validation_failed", "Sender and receiver addresses are required."));

        try
        {
            await BookDhlShipmentAsync(shipment, db, dhl, spaces, logger);
            var user = shipment.UserId.HasValue ? await db.Users.FindAsync(shipment.UserId.Value) : null;
            await emails.SendShipmentLabelCreatedAsync(shipment, user);
            return Results.Ok(MapToSummaryResponse(shipment));
        }
        catch (DhlException ex)
        {
            return Results.BadRequest(new ApiError(ex.ErrorCode, ex.Message));
        }
    }

    private static async Task<IResult> CancelShipment(
        Guid id,
        HttpContext ctx,
        AppDbContext db,
        NotificationEmailService emails)
    {
        var userId = GetUserId(ctx);
        var shipment = await db.Shipments.FirstOrDefaultAsync(s => s.Id == id && s.UserId == userId);

        if (shipment is null)
            return Results.NotFound(new ApiError("not_found", "Shipment not found."));

        var cancellableStatuses = new[] { "PendingPayment", "PaymentFailed", "Quoted" };
        if (!cancellableStatuses.Contains(shipment.Status))
            return Results.BadRequest(new ApiError("validation_failed",
                "Only shipments in PendingPayment or PaymentFailed status can be cancelled."));

        shipment.Status = "Cancelled";
        shipment.UpdatedAt = DateTime.UtcNow;

        db.ShipmentEvents.Add(new ShipmentEvent
        {
            ShipmentId = shipment.Id,
            EventType = "Cancelled",
            Description = "Shipment cancelled by customer."
        });

        await db.SaveChangesAsync();
        var user = await db.Users.FindAsync(userId);
        await emails.SendShipmentCancelledAsync(shipment, user);
        return Results.Ok(MapToSummaryResponse(shipment));
    }

    private static async Task<IResult> DownloadDocument(
        Guid id, string type, HttpContext ctx, AppDbContext db, SpacesStorageService spaces)
    {
        Func<Shipment, string?> getPath = type.ToLowerInvariant() switch
        {
            "label"   => s => s.LabelFilePath,
            "invoice" => s => s.InvoiceFilePath,
            "waybill" => s => s.WaybillFilePath,
            _         => throw new KeyNotFoundException($"Unknown document type: {type}")
        };
        return await DownloadFile(id, ctx, db, spaces, getPath, $"{type.ToLower()}.pdf");
    }

    private static async Task<IResult> DownloadFile(
        Guid id, HttpContext ctx, AppDbContext db, SpacesStorageService spaces,
        Func<Shipment, string?> getPath, string fileName)
    {
        var userId = GetUserId(ctx);
        var isAdmin = ctx.User.IsInRole("Admin");

        var shipment = await db.Shipments.FirstOrDefaultAsync(s => s.Id == id && (isAdmin || s.UserId == userId));
        if (shipment is null) return Results.NotFound(new ApiError("not_found", "Shipment not found."));

        var key = getPath(shipment);
        if (string.IsNullOrWhiteSpace(key))
            return Results.NotFound(new ApiError("not_found", "Document not yet available."));

        var bytes = await spaces.DownloadAsync(key);
        return Results.File(bytes, "application/pdf", fileName);
    }

    // ─── Internal helper called by webhook and create-label endpoint ────────

    public static async Task BookDhlShipmentAsync(
        Shipment shipment, AppDbContext db, DhlService dhl, SpacesStorageService spaces, ILogger logger)
    {
        var packages = await db.ShipmentPackages.Where(p => p.ShipmentId == shipment.Id).ToListAsync();
        var customsItems = await db.CustomsItems.Where(c => c.ShipmentId == shipment.Id).ToListAsync();

        if (shipment.SenderAddress is null || shipment.ReceiverAddress is null)
            throw new InvalidOperationException("Shipment addresses not loaded.");

        var dhlResponse = await dhl.CreateShipmentAsync(shipment, shipment.SenderAddress, shipment.ReceiverAddress, packages, customsItems);

        // Store tracking number
        shipment.DhlShipmentId = dhlResponse.DispatchConfirmationNumber;
        shipment.TrackingNumber = dhlResponse.ShipmentTrackingNumber
            ?? dhlResponse.Packages.FirstOrDefault()?.TrackingNumber;

        // Upload documents to DO Spaces
        foreach (var doc in dhlResponse.Documents)
        {
            if (string.IsNullOrWhiteSpace(doc.Content)) continue;

            try
            {
                var bytes = Convert.FromBase64String(doc.Content);
                var keyBase = $"shipments/{shipment.Id}";

                switch (doc.TypeCode.ToLower())
                {
                    case "label":
                        var labelKey = $"{keyBase}/label.pdf";
                        await spaces.UploadAsync(labelKey, bytes);
                        shipment.LabelFilePath = labelKey;
                        break;
                    case "invoice":
                        var invoiceKey = $"{keyBase}/invoice.pdf";
                        await spaces.UploadAsync(invoiceKey, bytes);
                        shipment.InvoiceFilePath = invoiceKey;
                        break;
                    case "waybilldoc":
                        var waybillKey = $"{keyBase}/waybill.pdf";
                        await spaces.UploadAsync(waybillKey, bytes);
                        shipment.WaybillFilePath = waybillKey;
                        break;
                    default:
                        logger.LogInformation("Unknown DHL document type: {TypeCode}", doc.TypeCode);
                        break;
                }
            }
            catch (FormatException ex)
            {
                logger.LogError(ex, "Failed to decode base64 for DHL document type {TypeCode}", doc.TypeCode);
            }
        }

        shipment.RawDhlShipmentResponse = System.Text.Json.JsonSerializer.Serialize(dhlResponse);
        shipment.Status = "LabelGenerated";
        shipment.UpdatedAt = DateTime.UtcNow;

        db.ShipmentEvents.Add(new ShipmentEvent
        {
            ShipmentId = shipment.Id,
            EventType = "LabelGenerated",
            Description = $"DHL shipment created. Tracking: {shipment.TrackingNumber}"
        });

        await db.SaveChangesAsync();
    }

    // ─── Mapping ───────────────────────────────────────────────────────────

    private static string MapStatus(string status) => status switch
    {
        "PendingPayment" or "PaymentFailed" => "pending_payment",
        "PaymentAuthorized" or "Booked"     => "paid",
        "LabelGenerated"    => "label_created",
        "DroppedOff"        => "dropped_off",
        "InTransit"         => "in_transit",
        "OutForDelivery"    => "out_for_delivery",
        "Delivered"         => "delivered",
        "Exception"         => "exception",
        "Cancelled"         => "cancelled",
        "Refunded"          => "refunded",
        _                   => status.ToLower()
    };

    private static string ServiceName(string productCode) =>
        productCode.Equals("D", StringComparison.OrdinalIgnoreCase)
            ? "DHL Express Documents"
            : "DHL Express Worldwide";

    private static ShipmentListItem MapToListItem(Shipment s)
    {
        var weightKg = s.Packages.FirstOrDefault()?.WeightKg ?? 0;
        var route    = $"{s.OriginCountry} → {s.DestinationCountry}";
        return new ShipmentListItem(
            s.Id, s.CreatedAt,
            ServiceName(s.ProductCode),
            route, weightKg,
            s.TotalAmount, s.Currency,
            MapStatus(s.Status),
            s.TrackingNumber);
    }

    private static ShipmentDetailResponse MapToDetailResponse(Shipment s)
    {
        var baseUrl = $"/api/shipments/{s.Id}/documents";
        var docs = new List<DocumentInfo>();
        docs.Add(new("label",   $"{baseUrl}/label",   s.LabelFilePath   is not null));
        docs.Add(new("invoice", $"{baseUrl}/invoice", s.InvoiceFilePath is not null));
        docs.Add(new("waybill", $"{baseUrl}/waybill", s.WaybillFilePath is not null));

        var latestPayment = s.Payments?.OrderByDescending(p => p.UpdatedAt).FirstOrDefault();
        var paymentInfo   = latestPayment is null ? null
            : new ShipmentPaymentInfo(latestPayment.Status, latestPayment.StripePaymentIntentId);

        return new ShipmentDetailResponse(
            s.Id,
            MapStatus(s.Status),
            s.TrackingNumber,
            docs,
            s.SenderAddress   is null ? null : MapAddress(s.SenderAddress),
            s.ReceiverAddress is null ? null : MapAddress(s.ReceiverAddress),
            s.CustomsItems?.Select(ci => new CustomsItemResponse(
                ci.Id, ci.Description, ci.Quantity, ci.UnitOfMeasurement,
                ci.UnitPrice, ci.Currency, ci.HsCode, ci.ManufacturerCountry,
                ci.NetWeightKg, ci.GrossWeightKg)).ToList() ?? new(),
            paymentInfo,
            s.TotalAmount,
            s.Currency,
            s.CreatedAt);
    }

    private static ShipmentResponse MapToSummaryResponse(Shipment s) => new(
        s.Id, s.UserId, s.TrackingNumber, MapStatus(s.Status),
        s.OriginCountry, s.DestinationCountry, s.ProductCode,
        s.TotalAmount, s.Currency,
        s.LabelFilePath   is not null ? $"/api/shipments/{s.Id}/documents/label"   : null,
        s.InvoiceFilePath is not null ? $"/api/shipments/{s.Id}/documents/invoice" : null,
        s.WaybillFilePath is not null ? $"/api/shipments/{s.Id}/documents/waybill" : null,
        s.CreatedAt, s.UpdatedAt);

    private static AddressResponse MapAddress(Address a) => new(
        a.Id, a.ContactName, a.CompanyName, a.Email, a.Phone,
        a.CountryCode, a.CityName, a.PostalCode,
        a.AddressLine1, a.AddressLine2, a.AddressLine3,
        a.IsDefaultSender, a.CreatedAt);

    private static Guid GetUserId(HttpContext ctx)
    {
        var sub = ctx.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? ctx.User.FindFirstValue("sub")
            ?? throw new UnauthorizedAccessException();
        return Guid.Parse(sub);
    }
}
