using Microsoft.EntityFrameworkCore;
using RyveSwift.Api.Common;
using RyveSwift.Api.Data;
using RyveSwift.Api.Dtos;
using RyveSwift.Api.Entities;

namespace RyveSwift.Api.Endpoints;

public static class AdminEndpoints
{
    public static void MapAdminEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/admin").WithTags("Admin").RequireAuthorization("AdminOnly");

        group.MapGet("/shipments", GetAllShipments)
            .WithName("AdminGetShipments")
            .WithSummary("Get all shipments with user info");

        group.MapGet("/users", GetAllUsers)
            .WithName("AdminGetUsers")
            .WithSummary("List all users");

        group.MapGet("/reports/revenue", GetRevenueReport)
            .WithName("GetRevenueReport")
            .WithSummary("Revenue report with date range filter");

        group.MapGet("/markup-rules", GetMarkupRules)
            .WithName("GetMarkupRules")
            .WithSummary("List all markup rules");

        group.MapPost("/markup-rules", CreateMarkupRule)
            .WithName("CreateMarkupRule")
            .WithSummary("Create a new markup rule");

        group.MapPut("/markup-rules/{id:guid}", UpdateMarkupRule)
            .WithName("UpdateMarkupRule")
            .WithSummary("Update an existing markup rule");

        group.MapDelete("/markup-rules/{id:guid}", DeleteMarkupRule)
            .WithName("DeleteMarkupRule")
            .WithSummary("Deactivate a markup rule");

        group.MapGet("/dhl-failures", GetDhlFailures)
            .WithName("GetDhlFailures")
            .WithSummary("Get shipment events for DHL booking failures");
    }

    private static async Task<IResult> GetAllShipments(
        AppDbContext db, int page = 1, int pageSize = 50, string? status = null)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var query = db.Shipments
            .Include(s => s.User)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(s => s.Status == status);

        var total = await query.CountAsync();
        var shipments = await query
            .OrderByDescending(s => s.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(s => new AdminShipmentResponse(
                s.Id, s.UserId,
                s.User != null ? s.User.Email : null,
                s.TrackingNumber, s.Status,
                s.OriginCountry, s.DestinationCountry,
                s.TotalAmount, s.Currency, s.CreatedAt))
            .ToListAsync();

        return Results.Ok(new PaginatedResult<AdminShipmentResponse>(shipments, total, page, pageSize));
    }

    private static async Task<IResult> GetAllUsers(AppDbContext db, int page = 1, int pageSize = 50)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var total = await db.Users.CountAsync();
        var users = await db.Users
            .OrderByDescending(u => u.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(u => new AdminUserResponse(u.Id, u.Email, u.FullName, u.Phone, u.Role, u.CreatedAt, u.LastLogin))
            .ToListAsync();

        return Results.Ok(new PaginatedResult<AdminUserResponse>(users, total, page, pageSize));
    }

    private static async Task<IResult> GetRevenueReport(
        AppDbContext db,
        DateTime? from = null,
        DateTime? to = null)
    {
        var fromDate = from?.ToUniversalTime() ?? DateTime.UtcNow.AddMonths(-1);
        var toDate = to?.ToUniversalTime() ?? DateTime.UtcNow;

        var paidStatuses = new[] { "LabelGenerated", "DroppedOff", "InTransit", "Delivered", "PaymentAuthorized" };

        var shipments = await db.Shipments
            .Where(s => paidStatuses.Contains(s.Status) &&
                        s.CreatedAt >= fromDate && s.CreatedAt <= toDate)
            .ToListAsync();

        var revenue = shipments.Sum(s => s.TotalAmount);
        var dhlCosts = shipments.Sum(s => s.DhlBaseRate ?? 0);
        var markup = revenue - dhlCosts;

        return Results.Ok(new
        {
            TotalRevenue = revenue,
            DhlBaseCost = dhlCosts,
            MarkupEarned = markup,
            TotalShipments = shipments.Count,
            Currency = "CAD",
            From = fromDate,
            To = toDate
        });
    }

    private static async Task<IResult> GetMarkupRules(AppDbContext db)
    {
        var rules = await db.MarkupRules
            .OrderBy(r => r.OriginCountry).ThenBy(r => r.DestinationCountry)
            .Select(r => new MarkupRuleResponse(
                r.Id, r.OriginCountry, r.DestinationCountry,
                r.MinWeightKg, r.MaxWeightKg, r.ProductCode,
                r.MarkupPercent, r.PlatformFee, r.IsActive, r.CreatedAt))
            .ToListAsync();

        return Results.Ok(rules);
    }

    private static async Task<IResult> CreateMarkupRule(MarkupRuleRequest req, AppDbContext db)
    {
        if (req.MarkupPercent < 0)
            return Results.BadRequest(new ApiError("VALIDATION_FAILED", "Markup percent cannot be negative."));

        var rule = new MarkupRule
        {
            OriginCountry = req.OriginCountry?.ToUpper(),
            DestinationCountry = req.DestinationCountry?.ToUpper(),
            MinWeightKg = req.MinWeightKg,
            MaxWeightKg = req.MaxWeightKg,
            ProductCode = req.ProductCode?.ToUpper(),
            MarkupPercent = req.MarkupPercent,
            PlatformFee = req.PlatformFee,
            IsActive = true
        };

        db.MarkupRules.Add(rule);
        await db.SaveChangesAsync();

        return Results.Created($"/api/admin/markup-rules/{rule.Id}",
            new MarkupRuleResponse(rule.Id, rule.OriginCountry, rule.DestinationCountry,
                rule.MinWeightKg, rule.MaxWeightKg, rule.ProductCode,
                rule.MarkupPercent, rule.PlatformFee, rule.IsActive, rule.CreatedAt));
    }

    private static async Task<IResult> UpdateMarkupRule(Guid id, MarkupRuleRequest req, AppDbContext db)
    {
        var rule = await db.MarkupRules.FindAsync(id);
        if (rule is null) return Results.NotFound(new ApiError("NOT_FOUND", "Markup rule not found."));

        if (req.MarkupPercent < 0)
            return Results.BadRequest(new ApiError("VALIDATION_FAILED", "Markup percent cannot be negative."));

        rule.OriginCountry = req.OriginCountry?.ToUpper();
        rule.DestinationCountry = req.DestinationCountry?.ToUpper();
        rule.MinWeightKg = req.MinWeightKg;
        rule.MaxWeightKg = req.MaxWeightKg;
        rule.ProductCode = req.ProductCode?.ToUpper();
        rule.MarkupPercent = req.MarkupPercent;
        rule.PlatformFee = req.PlatformFee;

        await db.SaveChangesAsync();
        return Results.Ok(new MarkupRuleResponse(rule.Id, rule.OriginCountry, rule.DestinationCountry,
            rule.MinWeightKg, rule.MaxWeightKg, rule.ProductCode,
            rule.MarkupPercent, rule.PlatformFee, rule.IsActive, rule.CreatedAt));
    }

    private static async Task<IResult> DeleteMarkupRule(Guid id, AppDbContext db)
    {
        var rule = await db.MarkupRules.FindAsync(id);
        if (rule is null) return Results.NotFound(new ApiError("NOT_FOUND", "Markup rule not found."));

        rule.IsActive = false;
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    private static async Task<IResult> GetDhlFailures(AppDbContext db, int limit = 50)
    {
        limit = Math.Clamp(limit, 1, 200);

        var failures = await db.ShipmentEvents
            .Include(e => e.Shipment)
            .Where(e => e.EventType == "DhlBookingFailed")
            .OrderByDescending(e => e.CreatedAt)
            .Take(limit)
            .Select(e => new
            {
                e.Id,
                ShipmentId = e.ShipmentId,
                TrackingNumber = e.Shipment != null ? e.Shipment.TrackingNumber : null,
                e.Description,
                e.CreatedAt
            })
            .ToListAsync();

        return Results.Ok(failures);
    }
}
