using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using RyveSwift.Api.Common;
using RyveSwift.Api.Data;
using RyveSwift.Api.Dtos;

namespace RyveSwift.Api.Endpoints;

public static class UserEndpoints
{
    public static void MapUserEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/users").WithTags("Users").RequireAuthorization();

        group.MapGet("/profile", GetProfile)
            .WithName("GetUserProfile")
            .WithSummary("Get the current user's profile");

        group.MapPut("/profile", UpdateProfile)
            .WithName("UpdateUserProfile")
            .WithSummary("Update the current user's profile");
    }

    private static async Task<IResult> GetProfile(HttpContext ctx, AppDbContext db)
    {
        var userId = GetUserId(ctx);
        var user = await db.Users.FindAsync(userId);
        if (user is null) return Results.NotFound(new ApiError("NOT_FOUND", "User not found."));

        return Results.Ok(new UserProfileResponse(user.Id, user.Email, user.Phone, user.FullName, user.Role, user.CreatedAt));
    }

    private static async Task<IResult> UpdateProfile(
        UpdateProfileRequest req, HttpContext ctx, AppDbContext db)
    {
        var userId = GetUserId(ctx);
        var user = await db.Users.FindAsync(userId);
        if (user is null) return Results.NotFound(new ApiError("NOT_FOUND", "User not found."));

        if (req.FullName is not null) user.FullName = req.FullName.Trim();
        if (req.Phone is not null) user.Phone = req.Phone.Trim();

        await db.SaveChangesAsync();
        return Results.Ok(new UserProfileResponse(user.Id, user.Email, user.Phone, user.FullName, user.Role, user.CreatedAt));
    }

    private static Guid GetUserId(HttpContext ctx)
    {
        var sub = ctx.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? ctx.User.FindFirstValue("sub")
            ?? throw new UnauthorizedAccessException();
        return Guid.Parse(sub);
    }
}
