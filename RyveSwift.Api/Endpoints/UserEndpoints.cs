using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using RyveSwift.Api.Common;
using RyveSwift.Api.Data;
using RyveSwift.Api.Dtos;
using RyveSwift.Api.Entities;
using RyveSwift.Api.Services;

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

        group.MapPost("/change-password", ChangePassword)
            .WithName("ChangePassword")
            .WithSummary("Change the current user's password");
    }

    private static async Task<IResult> GetProfile(HttpContext ctx, AppDbContext db)
    {
        var userId = GetUserId(ctx);
        var user = await db.Users.FindAsync(userId);
        if (user is null) return Results.NotFound(new ApiError("NOT_FOUND", "User not found."));

        return Results.Ok(MapProfile(user));
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
        return Results.Ok(MapProfile(user));
    }

    private static async Task<IResult> ChangePassword(
        ChangePasswordRequest req,
        HttpContext ctx,
        AppDbContext db,
        NotificationEmailService emails)
    {
        var userId = GetUserId(ctx);
        var user = await db.Users.FindAsync(userId);
        if (user is null) return Results.NotFound(new ApiError("NOT_FOUND", "User not found."));

        if (string.IsNullOrWhiteSpace(req.CurrentPassword) || string.IsNullOrWhiteSpace(req.NewPassword))
            return Results.BadRequest(new ApiError("VALIDATION_FAILED", "Current password and new password are required."));

        if (req.NewPassword.Length < 8)
            return Results.BadRequest(new ApiError("VALIDATION_FAILED", "New password must be at least 8 characters."));

        if (!BCrypt.Net.BCrypt.Verify(req.CurrentPassword, user.PasswordHash))
            return Results.BadRequest(new ApiError("VALIDATION_FAILED", "Current password is incorrect."));

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.NewPassword);
        user.PasswordResetRequired = false;
        user.PasswordChangedAt = DateTime.UtcNow;

        var refreshTokens = await db.UserRefreshTokens
            .Where(t => t.UserId == user.Id && !t.IsRevoked)
            .ToListAsync();
        refreshTokens.ForEach(t => t.IsRevoked = true);

        await db.SaveChangesAsync();
        await emails.SendPasswordChangedEmailAsync(user);
        return Results.Ok(MapProfile(user));
    }

    private static UserProfileResponse MapProfile(User user) =>
        new(
            user.Id,
            user.Email,
            user.Phone,
            user.FullName,
            user.Role,
            user.PasswordResetRequired,
            user.EmailUnsubscribedAt,
            user.CreatedAt);

    private static Guid GetUserId(HttpContext ctx)
    {
        var sub = ctx.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? ctx.User.FindFirstValue("sub")
            ?? throw new UnauthorizedAccessException();
        return Guid.Parse(sub);
    }
}
