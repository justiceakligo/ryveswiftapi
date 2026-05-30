using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using RyveSwift.Api.Common;
using RyveSwift.Api.Data;
using RyveSwift.Api.Dtos;
using RyveSwift.Api.Entities;

namespace RyveSwift.Api.Endpoints;

public static class AddressEndpoints
{
    public static void MapAddressEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/addresses").WithTags("Addresses").RequireAuthorization();

        group.MapGet("/", GetAddresses).WithName("GetAddresses").WithSummary("List saved addresses");
        group.MapPost("/", CreateAddress).WithName("CreateAddress").WithSummary("Create a new address");
        group.MapPut("/{id:guid}", UpdateAddress).WithName("UpdateAddress").WithSummary("Update an address");
        group.MapDelete("/{id:guid}", DeleteAddress).WithName("DeleteAddress").WithSummary("Delete an address");
    }

    private static async Task<IResult> GetAddresses(HttpContext ctx, AppDbContext db)
    {
        var userId = GetUserId(ctx);
        var addresses = await db.Addresses
            .Where(a => a.UserId == userId)
            .OrderByDescending(a => a.IsDefaultSender)
            .ThenByDescending(a => a.CreatedAt)
            .Select(a => MapToResponse(a))
            .ToListAsync();

        return Results.Ok(addresses);
    }

    private static async Task<IResult> CreateAddress(
        CreateAddressRequest req, HttpContext ctx, AppDbContext db)
    {
        if (string.IsNullOrWhiteSpace(req.ContactName))
            return Results.BadRequest(new ApiError("VALIDATION_FAILED", "Contact name is required."));
        if (string.IsNullOrWhiteSpace(req.Phone))
            return Results.BadRequest(new ApiError("VALIDATION_FAILED", "Phone number is required."));
        if (string.IsNullOrWhiteSpace(req.CountryCode) || req.CountryCode.Length != 2)
            return Results.BadRequest(new ApiError("VALIDATION_FAILED", "Valid 2-letter country code is required."));
        if (string.IsNullOrWhiteSpace(req.AddressLine1))
            return Results.BadRequest(new ApiError("VALIDATION_FAILED", "Address line 1 is required."));

        var userId = GetUserId(ctx);

        // If new address is default sender, clear old defaults
        if (req.IsDefaultSender)
        {
            var defaults = await db.Addresses.Where(a => a.UserId == userId && a.IsDefaultSender).ToListAsync();
            defaults.ForEach(a => a.IsDefaultSender = false);
        }

        var address = new Address
        {
            UserId = userId,
            ContactName = req.ContactName.Trim(),
            CompanyName = req.CompanyName?.Trim(),
            Email = req.Email?.Trim(),
            Phone = req.Phone.Trim(),
            CountryCode = req.CountryCode.ToUpper(),
            CityName = req.CityName.Trim(),
            PostalCode = req.PostalCode?.Trim(),
            AddressLine1 = req.AddressLine1.Trim(),
            AddressLine2 = req.AddressLine2?.Trim(),
            AddressLine3 = req.AddressLine3?.Trim(),
            IsDefaultSender = req.IsDefaultSender
        };

        db.Addresses.Add(address);
        await db.SaveChangesAsync();
        return Results.Created($"/api/addresses/{address.Id}", MapToResponse(address));
    }

    private static async Task<IResult> UpdateAddress(
        Guid id, UpdateAddressRequest req, HttpContext ctx, AppDbContext db)
    {
        var userId = GetUserId(ctx);
        var address = await db.Addresses.FirstOrDefaultAsync(a => a.Id == id && a.UserId == userId);
        if (address is null) return Results.NotFound(new ApiError("NOT_FOUND", "Address not found."));

        if (req.IsDefaultSender && !address.IsDefaultSender)
        {
            var defaults = await db.Addresses.Where(a => a.UserId == userId && a.IsDefaultSender).ToListAsync();
            defaults.ForEach(a => a.IsDefaultSender = false);
        }

        address.ContactName = req.ContactName.Trim();
        address.CompanyName = req.CompanyName?.Trim();
        address.Email = req.Email?.Trim();
        address.Phone = req.Phone.Trim();
        address.CountryCode = req.CountryCode.ToUpper();
        address.CityName = req.CityName.Trim();
        address.PostalCode = req.PostalCode?.Trim();
        address.AddressLine1 = req.AddressLine1.Trim();
        address.AddressLine2 = req.AddressLine2?.Trim();
        address.AddressLine3 = req.AddressLine3?.Trim();
        address.IsDefaultSender = req.IsDefaultSender;

        await db.SaveChangesAsync();
        return Results.Ok(MapToResponse(address));
    }

    private static async Task<IResult> DeleteAddress(Guid id, HttpContext ctx, AppDbContext db)
    {
        var userId = GetUserId(ctx);
        var address = await db.Addresses.FirstOrDefaultAsync(a => a.Id == id && a.UserId == userId);
        if (address is null) return Results.NotFound(new ApiError("NOT_FOUND", "Address not found."));

        db.Addresses.Remove(address);
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    private static AddressResponse MapToResponse(Address a) => new(
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
