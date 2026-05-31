using Microsoft.EntityFrameworkCore;
using RyveSwift.Api.Common;
using RyveSwift.Api.Data;
using RyveSwift.Api.Dtos;
using RyveSwift.Api.Services;

namespace RyveSwift.Api.Endpoints;

public static class EmailPreferenceEndpoints
{
    public static void MapEmailPreferenceEndpoints(this WebApplication app)
    {
        app.MapGet("/api/email/unsubscribe", UnsubscribeFromLink)
            .WithTags("Email")
            .WithName("UnsubscribeFromEmail")
            .WithSummary("Unsubscribe from non-essential user email notifications")
            .AllowAnonymous();

        app.MapPost("/api/email/unsubscribe", Unsubscribe)
            .WithTags("Email")
            .WithName("UnsubscribeFromEmailPost")
            .WithSummary("Unsubscribe from non-essential user email notifications")
            .AllowAnonymous();

        app.MapPost("/api/email/resubscribe", Resubscribe)
            .WithTags("Email")
            .WithName("ResubscribeToEmail")
            .WithSummary("Resubscribe to user email notifications")
            .AllowAnonymous();

        app.MapGet("/api/email/resubscribe", ResubscribeFromLink)
            .WithTags("Email")
            .WithName("ResubscribeToEmailFromLink")
            .WithSummary("Resubscribe to user email notifications")
            .AllowAnonymous();
    }

    private static async Task<IResult> UnsubscribeFromLink(
        string token,
        AppDbContext db,
        EmailPreferenceTokenService tokenService)
    {
        var result = await SetPreference(token, unsubscribe: true, db, tokenService);
        if (result is null)
        {
            return Results.Content(
                "<!doctype html><html><body><h1>Invalid link</h1><p>This unsubscribe link is invalid.</p></body></html>",
                "text/html",
                statusCode: StatusCodes.Status400BadRequest);
        }

        return Results.Content(
            "<!doctype html><html><body><h1>Unsubscribed</h1><p>You have been unsubscribed from non-essential RyveSwift email notifications.</p></body></html>",
            "text/html");
    }

    private static async Task<IResult> Unsubscribe(
        EmailPreferenceTokenRequest req,
        AppDbContext db,
        EmailPreferenceTokenService tokenService)
    {
        var result = await SetPreference(req.Token, unsubscribe: true, db, tokenService);
        return result is null
            ? Results.BadRequest(new ApiError("VALIDATION_FAILED", "Invalid unsubscribe token."))
            : Results.Ok(result);
    }

    private static async Task<IResult> Resubscribe(
        EmailPreferenceTokenRequest req,
        AppDbContext db,
        EmailPreferenceTokenService tokenService)
    {
        var result = await SetPreference(req.Token, unsubscribe: false, db, tokenService);
        return result is null
            ? Results.BadRequest(new ApiError("VALIDATION_FAILED", "Invalid resubscribe token."))
            : Results.Ok(result);
    }

    private static async Task<IResult> ResubscribeFromLink(
        string token,
        AppDbContext db,
        EmailPreferenceTokenService tokenService)
    {
        var result = await SetPreference(token, unsubscribe: false, db, tokenService);
        if (result is null)
        {
            return Results.Content(
                "<!doctype html><html><body><h1>Invalid link</h1><p>This resubscribe link is invalid.</p></body></html>",
                "text/html",
                statusCode: StatusCodes.Status400BadRequest);
        }

        return Results.Content(
            "<!doctype html><html><body><h1>Resubscribed</h1><p>You have been resubscribed to RyveSwift email notifications.</p></body></html>",
            "text/html");
    }

    private static async Task<EmailPreferenceResponse?> SetPreference(
        string token,
        bool unsubscribe,
        AppDbContext db,
        EmailPreferenceTokenService tokenService)
    {
        if (!tokenService.TryValidate(token, out var userId))
            return null;

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId && !u.DeletedAt.HasValue);
        if (user is null)
            return null;

        user.EmailUnsubscribedAt = unsubscribe ? DateTime.UtcNow : null;
        await db.SaveChangesAsync();

        return new EmailPreferenceResponse(
            user.EmailUnsubscribedAt.HasValue,
            user.EmailUnsubscribedAt,
            unsubscribe
                ? "You have been unsubscribed from non-essential RyveSwift email notifications."
                : "You have been resubscribed to RyveSwift email notifications.");
    }
}
