using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
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

        group.MapPost("/forgot-password", ForgotPassword)
            .WithName("ForgotPassword")
            .WithSummary("Send a password reset email when the account exists")
            .RequireRateLimiting("auth");

        group.MapPost("/reset-password", ResetPassword)
            .WithName("ResetPassword")
            .WithSummary("Reset a password using an email reset token")
            .RequireRateLimiting("auth");

        group.MapPost("/refresh", RefreshToken)
            .WithName("RefreshToken")
            .WithSummary("Exchange a refresh token for a new access token")
            .RequireRateLimiting("auth");
    }

    private static async Task<IResult> Register(
        RegisterRequest req,
        AppDbContext db,
        JwtService jwt,
        ConfigService config,
        NotificationEmailService emails)
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
            Role = "Customer",
            PasswordChangedAt = DateTime.UtcNow
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
        await emails.SendWelcomeEmailAsync(user);
        await emails.SendNewUserAdminAlertAsync(user);

        var accessToken = jwt.GenerateAccessToken(user);
        var expiryMinutes = config.GetInt("JWT_EXPIRY_MINUTES", 60);

        return Results.Ok(new LoginResponse(accessToken, tokenStr, "Bearer", expiryMinutes * 60, user.PasswordResetRequired));
    }

    private static async Task<IResult> Login(
        LoginRequest req, AppDbContext db, JwtService jwt, ConfigService config)
    {
        if (string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.Password))
            return Results.BadRequest(new ApiError("VALIDATION_FAILED", "Email and password are required."));

        var user = await db.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == req.Email.ToLower());
        if (user is null || !BCrypt.Net.BCrypt.Verify(req.Password, user.PasswordHash))
            return Results.BadRequest(new ApiError("VALIDATION_FAILED", "Invalid email or password."));

        if (user.DeletedAt.HasValue)
            return Results.BadRequest(new ApiError("VALIDATION_FAILED", "Invalid email or password."));

        if (user.IsSuspended)
            return Results.Json(new ApiError("ACCOUNT_SUSPENDED", "This account is suspended. Contact support."),
                statusCode: StatusCodes.Status403Forbidden);

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

        return Results.Ok(new LoginResponse(accessToken, tokenStr, "Bearer", expiryMinutes * 60, user.PasswordResetRequired));
    }

    private static async Task<IResult> ForgotPassword(
        ForgotPasswordRequest req,
        AppDbContext db,
        ConfigService config,
        NotificationEmailService emails,
        HttpContext http)
    {
        if (string.IsNullOrWhiteSpace(req.Email) || !req.Email.Contains('@'))
            return Results.BadRequest(new ApiError("VALIDATION_FAILED", "A valid email is required."));

        var normalizedEmail = req.Email.Trim().ToLowerInvariant();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == normalizedEmail);
        if (user is not null && !user.DeletedAt.HasValue && !user.IsSuspended)
        {
            var now = DateTime.UtcNow;
            var oldTokens = await db.PasswordResetTokens
                .Where(t => t.UserId == user.Id && t.UsedAt == null && t.ExpiresAt > now)
                .ToListAsync();
            oldTokens.ForEach(t => t.UsedAt = now);

            var token = GeneratePasswordResetToken();
            var expiresMinutes = Math.Clamp(config.GetInt("Email:PasswordResetExpiryMinutes", 30), 5, 1440);
            var resetUrl = BuildPasswordResetUrl(config, token);

            db.PasswordResetTokens.Add(new PasswordResetToken
            {
                UserId = user.Id,
                TokenHash = HashPasswordResetToken(token),
                ExpiresAt = now.AddMinutes(expiresMinutes),
                CreatedIp = http.Connection.RemoteIpAddress?.ToString()
            });

            await db.SaveChangesAsync();
            await emails.SendPasswordResetLinkEmailAsync(user, resetUrl, expiresMinutes);
        }

        return Results.Ok(new MessageResponse("If an active account exists for that email, a password reset link has been sent."));
    }

    private static async Task<IResult> ResetPassword(
        ResetPasswordRequest req,
        AppDbContext db,
        NotificationEmailService emails)
    {
        if (string.IsNullOrWhiteSpace(req.Token))
            return Results.BadRequest(new ApiError("VALIDATION_FAILED", "Reset token is required."));

        if (string.IsNullOrWhiteSpace(req.NewPassword) || req.NewPassword.Length < 8)
            return Results.BadRequest(new ApiError("VALIDATION_FAILED", "New password must be at least 8 characters."));

        var tokenHash = HashPasswordResetToken(req.Token.Trim());
        var resetToken = await db.PasswordResetTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash && t.UsedAt == null);

        if (resetToken is null ||
            resetToken.ExpiresAt < DateTime.UtcNow ||
            resetToken.User.DeletedAt.HasValue ||
            resetToken.User.IsSuspended)
        {
            return Results.BadRequest(new ApiError("VALIDATION_FAILED", "The reset link is invalid or expired."));
        }

        resetToken.User.PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.NewPassword);
        resetToken.User.PasswordResetRequired = false;
        resetToken.User.PasswordChangedAt = DateTime.UtcNow;
        resetToken.UsedAt = DateTime.UtcNow;

        var activeRefreshTokens = await db.UserRefreshTokens
            .Where(t => t.UserId == resetToken.UserId && !t.IsRevoked)
            .ToListAsync();
        activeRefreshTokens.ForEach(t => t.IsRevoked = true);

        await db.SaveChangesAsync();
        await emails.SendPasswordChangedEmailAsync(resetToken.User);

        return Results.Ok(new MessageResponse("Password has been reset."));
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

        if (storedToken.User.DeletedAt.HasValue || storedToken.User.IsSuspended)
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

        return Results.Ok(new LoginResponse(accessToken, newTokenStr, "Bearer", expiryMinutes * 60, storedToken.User.PasswordResetRequired));
    }

    private static string BuildPasswordResetUrl(ConfigService config, string token)
    {
        var baseUrl = config.Get("App:FrontendBaseUrl", "https://swift.ryvepos.com").TrimEnd('/');
        return $"{baseUrl}/reset-password?token={Uri.EscapeDataString(token)}";
    }

    private static string GeneratePasswordResetToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static string HashPasswordResetToken(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
