using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using RyveSwift.Api.Common;
using RyveSwift.Api.Data;
using RyveSwift.Api.Dtos;
using RyveSwift.Api.Entities;
using RyveSwift.Api.Services;

namespace RyveSwift.Api.Endpoints;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/auth").WithTags("Auth");

        group.MapPost("/register", Register)
            .WithName("Register")
            .WithSummary("Register a new user account")
            .RequireRateLimiting("auth");

        group.MapPost("/login", Login)
            .WithName("Login")
            .WithSummary("Login and receive JWT tokens")
            .RequireRateLimiting("auth");

        group.MapPost("/refresh", RefreshToken)
            .WithName("RefreshToken")
            .WithSummary("Exchange a refresh token for a new access token")
            .RequireRateLimiting("auth");
    }

    private static async Task<IResult> Register(
        RegisterRequest req, AppDbContext db, JwtService jwt, ConfigService config)
    {
        if (string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.Password))
            return Results.BadRequest(new ApiError("VALIDATION_FAILED", "Email and password are required."));

        if (req.Password.Length < 8)
            return Results.BadRequest(new ApiError("VALIDATION_FAILED", "Password must be at least 8 characters."));

        if (await db.Users.AnyAsync(u => u.Email.ToLower() == req.Email.ToLower()))
            return Results.Conflict(new ApiError("VALIDATION_FAILED", "An account with this email already exists."));

        var user = new User
        {
            Email = req.Email.ToLower().Trim(),
            FullName = req.FullName?.Trim(),
            Phone = req.Phone?.Trim(),
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.Password),
            Role = "Customer"
        };

        db.Users.Add(user);

        var (tokenStr, tokenHash, expiresAt) = jwt.GenerateRefreshToken();
        db.UserRefreshTokens.Add(new UserRefreshToken
        {
            UserId = user.Id,
            TokenHash = tokenHash,
            ExpiresAt = expiresAt
        });

        await db.SaveChangesAsync();

        var accessToken = jwt.GenerateAccessToken(user);
        var expiryMinutes = config.GetInt("JWT_EXPIRY_MINUTES", 60);

        return Results.Ok(new LoginResponse(accessToken, tokenStr, "Bearer", expiryMinutes * 60));
    }

    private static async Task<IResult> Login(
        LoginRequest req, AppDbContext db, JwtService jwt, ConfigService config)
    {
        if (string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.Password))
            return Results.BadRequest(new ApiError("VALIDATION_FAILED", "Email and password are required."));

        var user = await db.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == req.Email.ToLower());
        if (user is null || !BCrypt.Net.BCrypt.Verify(req.Password, user.PasswordHash))
            return Results.BadRequest(new ApiError("VALIDATION_FAILED", "Invalid email or password."));

        user.LastLogin = DateTime.UtcNow;

        // Revoke old refresh tokens
        var oldTokens = await db.UserRefreshTokens
            .Where(t => t.UserId == user.Id && !t.IsRevoked)
            .ToListAsync();
        oldTokens.ForEach(t => t.IsRevoked = true);

        var (tokenStr, tokenHash, expiresAt) = jwt.GenerateRefreshToken();
        db.UserRefreshTokens.Add(new UserRefreshToken
        {
            UserId = user.Id,
            TokenHash = tokenHash,
            ExpiresAt = expiresAt
        });

        await db.SaveChangesAsync();

        var accessToken = jwt.GenerateAccessToken(user);
        var expiryMinutes = config.GetInt("JWT_EXPIRY_MINUTES", 60);

        return Results.Ok(new LoginResponse(accessToken, tokenStr, "Bearer", expiryMinutes * 60));
    }

    private static async Task<IResult> RefreshToken(
        RefreshTokenRequest req, AppDbContext db, JwtService jwt, ConfigService config)
    {
        if (string.IsNullOrWhiteSpace(req.RefreshToken))
            return Results.BadRequest(new ApiError("VALIDATION_FAILED", "Refresh token is required."));

        var tokenHash = jwt.HashRefreshToken(req.RefreshToken);

        var storedToken = await db.UserRefreshTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash && !t.IsRevoked);

        if (storedToken is null || storedToken.ExpiresAt < DateTime.UtcNow)
            return Results.Unauthorized();

        if (storedToken.User is null)
            return Results.Unauthorized();

        // Rotate: revoke old, issue new
        storedToken.IsRevoked = true;

        var (newTokenStr, newTokenHash, newExpiresAt) = jwt.GenerateRefreshToken();
        db.UserRefreshTokens.Add(new UserRefreshToken
        {
            UserId = storedToken.UserId,
            TokenHash = newTokenHash,
            ExpiresAt = newExpiresAt
        });

        await db.SaveChangesAsync();

        var accessToken = jwt.GenerateAccessToken(storedToken.User);
        var expiryMinutes = config.GetInt("JWT_EXPIRY_MINUTES", 60);

        return Results.Ok(new LoginResponse(accessToken, newTokenStr, "Bearer", expiryMinutes * 60));
    }
}
