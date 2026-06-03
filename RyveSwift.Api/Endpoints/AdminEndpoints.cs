using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using RyveSwift.Api.Common;
using RyveSwift.Api.Data;
using RyveSwift.Api.Dhl;
using RyveSwift.Api.Dtos;
using RyveSwift.Api.Entities;
using RyveSwift.Api.Services;

namespace RyveSwift.Api.Endpoints;

public static class AdminEndpoints
{
    private static readonly HashSet<string> RevenueStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "PaymentAuthorized",
        "Booked",
        "LabelGenerated",
        "DroppedOff",
        "InTransit",
        "OutForDelivery",
        "Delivered",
        "Exception"
    };

    public static void MapAdminEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/admin").WithTags("Admin").RequireAuthorization("AdminOnly");

        group.MapGet("/shipments", GetAllShipments)
            .WithName("AdminGetShipments")
            .WithSummary("Get all shipments with user info");

        group.MapGet("/users", GetAllUsers)
            .WithName("AdminGetUsers")
            .WithSummary("List all users");

        group.MapGet("/users/{id:guid}", GetUser)
            .WithName("AdminGetUser")
            .WithSummary("Get user details and lifetime account metrics");

        group.MapPost("/users", CreateUser)
            .WithName("AdminCreateUser")
            .WithSummary("Create a user account");

        group.MapPut("/users/{id:guid}", UpdateUser)
            .WithName("AdminUpdateUser")
            .WithSummary("Update a user account");

        group.MapPost("/users/{id:guid}/suspend", SuspendUser)
            .WithName("AdminSuspendUser")
            .WithSummary("Suspend a user account and revoke refresh tokens");

        group.MapPost("/users/{id:guid}/reactivate", ReactivateUser)
            .WithName("AdminReactivateUser")
            .WithSummary("Reactivate a suspended or soft-deleted user account");

        group.MapDelete("/users/{id:guid}", DeleteUser)
            .WithName("AdminDeleteUser")
            .WithSummary("Soft-delete a user account and revoke refresh tokens");

        group.MapPost("/users/{id:guid}/reset-password", ResetUserPassword)
            .WithName("AdminResetUserPassword")
            .WithSummary("Reset a user's password and revoke refresh tokens");

        group.MapGet("/reports/revenue", GetRevenueReport)
            .WithName("GetRevenueReport")
            .WithSummary("Revenue report with date range filter");

        group.MapGet("/analytics/overview", GetRevenueReport)
            .WithName("GetAdminAnalyticsOverview")
            .WithSummary("Admin dashboard analytics with revenue, DHL cost, markup, and platform metrics");

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

        group.MapPost("/emails/test", SendTestEmail)
            .WithName("AdminSendTestEmail")
            .WithSummary("Send a test email to verify transactional email configuration");

        group.MapPost("/emails/send", SendCustomEmail)
            .WithName("AdminSendCustomEmail")
            .WithSummary("Send a custom transactional email");

        group.MapGet("/emails/config", GetEmailConfig)
            .WithName("AdminGetEmailConfig")
            .WithSummary("Get transactional email configuration status");

        group.MapPut("/emails/config", UpdateEmailConfig)
            .WithName("AdminUpdateEmailConfig")
            .WithSummary("Update transactional email configuration");
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
                s.ProductCode,
                s.TotalAmount,
                s.DhlBaseRate,
                s.MarkupPercent,
                s.PlatformFee,
                s.TotalAmount - (s.DhlBaseRate ?? 0) - (s.PlatformFee ?? 0),
                s.TotalAmount - (s.DhlBaseRate ?? 0),
                s.Currency, s.CreatedAt))
            .ToListAsync();

        return Results.Ok(new PaginatedResult<AdminShipmentResponse>(shipments, total, page, pageSize));
    }

    private static async Task<IResult> GetAllUsers(
        AppDbContext db,
        int page = 1,
        int pageSize = 50,
        bool includeDeleted = false)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var query = db.Users.AsQueryable();
        if (!includeDeleted)
            query = query.Where(u => !u.DeletedAt.HasValue);

        var total = await query.CountAsync();
        var userEntities = await query
            .OrderByDescending(u => u.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();
        var users = userEntities.Select(MapAdminUser).ToList();

        return Results.Ok(new PaginatedResult<AdminUserResponse>(users, total, page, pageSize));
    }

    private static async Task<IResult> GetUser(Guid id, AppDbContext db)
    {
        var user = await db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == id);

        if (user is null)
            return Results.NotFound(new ApiError("NOT_FOUND", "User not found."));

        var shipments = await db.Shipments
            .AsNoTracking()
            .Where(s => s.UserId == id)
            .ToListAsync();

        return Results.Ok(new AdminUserDetailResponse(
            user.Id,
            user.Email,
            user.FullName,
            user.Phone,
            user.Role,
            user.IsSuspended,
            user.SuspendedAt,
            user.SuspendedReason,
            user.DeletedAt,
            user.PasswordResetRequired,
            user.PasswordChangedAt,
            user.EmailUnsubscribedAt,
            user.CreatedAt,
            user.LastLogin,
            shipments.Count,
            Money(shipments.Where(s => RevenueStatuses.Contains(s.Status)).Sum(s => s.TotalAmount)),
            shipments.Count == 0 ? null : shipments.Max(s => (DateTime?)s.CreatedAt)));
    }

    private static async Task<IResult> CreateUser(
        AdminCreateUserRequest req,
        AppDbContext db,
        NotificationEmailService emails)
    {
        var validationError = ValidateUserMutation(req.Email, req.Password, req.Role, isCreate: true);
        if (validationError is not null) return validationError;

        var email = NormalizeEmail(req.Email);
        if (await db.Users.AnyAsync(u => u.Email == email))
            return Results.Conflict(new ApiError("VALIDATION_FAILED", "An account with this email already exists."));

        var generatedPassword = string.IsNullOrWhiteSpace(req.Password)
            ? GenerateTemporaryPassword()
            : null;
        var password = req.Password ?? generatedPassword!;

        var user = new User
        {
            Email = email,
            FullName = req.FullName?.Trim(),
            Phone = req.Phone?.Trim(),
            Role = NormalizeRole(req.Role),
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            PasswordResetRequired = generatedPassword is not null,
            PasswordChangedAt = DateTime.UtcNow
        };

        db.Users.Add(user);
        await db.SaveChangesAsync();
        await emails.SendAdminUserCreatedEmailAsync(user, generatedPassword);

        return Results.Created($"/api/admin/users/{user.Id}",
            new AdminCreateUserResponse(MapAdminUser(user), generatedPassword));
    }

    private static async Task<IResult> UpdateUser(
        Guid id,
        AdminUpdateUserRequest req,
        HttpContext ctx,
        AppDbContext db)
    {
        var user = await db.Users.FindAsync(id);
        if (user is null)
            return Results.NotFound(new ApiError("NOT_FOUND", "User not found."));

        var validationError = ValidateUserMutation(req.Email, null, req.Role, isCreate: false);
        if (validationError is not null) return validationError;

        if (!string.IsNullOrWhiteSpace(req.Email))
        {
            var email = NormalizeEmail(req.Email);
            var exists = await db.Users.AnyAsync(u => u.Id != id && u.Email == email);
            if (exists)
                return Results.Conflict(new ApiError("VALIDATION_FAILED", "An account with this email already exists."));
            user.Email = email;
        }

        if (req.FullName is not null) user.FullName = req.FullName.Trim();
        if (req.Phone is not null) user.Phone = req.Phone.Trim();

        if (!string.IsNullOrWhiteSpace(req.Role))
        {
            var newRole = NormalizeRole(req.Role);
            if (!user.Role.Equals(newRole, StringComparison.OrdinalIgnoreCase) &&
                user.Role.Equals("Admin", StringComparison.OrdinalIgnoreCase) &&
                !await HasAnotherActiveAdmin(db, user.Id))
            {
                return Results.BadRequest(new ApiError("VALIDATION_FAILED", "Cannot remove the last active admin."));
            }

            user.Role = newRole;
        }

        await db.SaveChangesAsync();
        return Results.Ok(MapAdminUser(user));
    }

    private static async Task<IResult> SuspendUser(
        Guid id,
        AdminSuspendUserRequest req,
        HttpContext ctx,
        AppDbContext db,
        NotificationEmailService emails)
    {
        var user = await db.Users.FindAsync(id);
        if (user is null)
            return Results.NotFound(new ApiError("NOT_FOUND", "User not found."));

        if (id == GetActorUserId(ctx))
            return Results.BadRequest(new ApiError("VALIDATION_FAILED", "Admins cannot suspend their own account."));

        if (user.Role.Equals("Admin", StringComparison.OrdinalIgnoreCase) &&
            !await HasAnotherActiveAdmin(db, user.Id))
        {
            return Results.BadRequest(new ApiError("VALIDATION_FAILED", "Cannot suspend the last active admin."));
        }

        user.IsSuspended = true;
        user.SuspendedAt = DateTime.UtcNow;
        user.SuspendedReason = string.IsNullOrWhiteSpace(req.Reason) ? null : req.Reason.Trim();
        await RevokeRefreshTokens(user.Id, db);
        await db.SaveChangesAsync();
        await emails.SendAccountSuspendedEmailAsync(user, user.SuspendedReason);

        return Results.Ok(MapAdminUser(user));
    }

    private static async Task<IResult> ReactivateUser(
        Guid id,
        AppDbContext db,
        NotificationEmailService emails)
    {
        var user = await db.Users.FindAsync(id);
        if (user is null)
            return Results.NotFound(new ApiError("NOT_FOUND", "User not found."));

        user.IsSuspended = false;
        user.SuspendedAt = null;
        user.SuspendedReason = null;
        user.DeletedAt = null;

        await db.SaveChangesAsync();
        await emails.SendAccountReactivatedEmailAsync(user);
        return Results.Ok(MapAdminUser(user));
    }

    private static async Task<IResult> DeleteUser(
        Guid id,
        HttpContext ctx,
        AppDbContext db,
        NotificationEmailService emails)
    {
        var user = await db.Users.FindAsync(id);
        if (user is null)
            return Results.NotFound(new ApiError("NOT_FOUND", "User not found."));

        if (id == GetActorUserId(ctx))
            return Results.BadRequest(new ApiError("VALIDATION_FAILED", "Admins cannot delete their own account."));

        if (user.Role.Equals("Admin", StringComparison.OrdinalIgnoreCase) &&
            !await HasAnotherActiveAdmin(db, user.Id))
        {
            return Results.BadRequest(new ApiError("VALIDATION_FAILED", "Cannot delete the last active admin."));
        }

        user.DeletedAt = DateTime.UtcNow;
        user.IsSuspended = true;
        user.SuspendedAt ??= DateTime.UtcNow;
        user.SuspendedReason = "Account deleted by admin.";
        await RevokeRefreshTokens(user.Id, db);
        await db.SaveChangesAsync();
        await emails.SendAccountDeletedEmailAsync(user);

        return Results.Ok(MapAdminUser(user));
    }

    private static async Task<IResult> ResetUserPassword(
        Guid id,
        AdminResetPasswordRequest req,
        AppDbContext db,
        NotificationEmailService emails)
    {
        var user = await db.Users.FindAsync(id);
        if (user is null)
            return Results.NotFound(new ApiError("NOT_FOUND", "User not found."));

        if (user.DeletedAt.HasValue)
            return Results.BadRequest(new ApiError("VALIDATION_FAILED", "Cannot reset password for a deleted user."));

        if (!string.IsNullOrWhiteSpace(req.NewPassword) && req.NewPassword.Length < 8)
            return Results.BadRequest(new ApiError("VALIDATION_FAILED", "New password must be at least 8 characters."));

        var generatedPassword = string.IsNullOrWhiteSpace(req.NewPassword)
            ? GenerateTemporaryPassword()
            : null;
        var password = req.NewPassword ?? generatedPassword!;

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(password);
        user.PasswordResetRequired = req.RequirePasswordChange ?? true;
        user.PasswordChangedAt = DateTime.UtcNow;
        await RevokeRefreshTokens(user.Id, db);
        await db.SaveChangesAsync();
        await emails.SendPasswordResetEmailAsync(user, generatedPassword, user.PasswordResetRequired);

        return Results.Ok(new AdminResetPasswordResponse(
            user.Id,
            user.Email,
            user.PasswordResetRequired,
            generatedPassword));
    }

    private static async Task<IResult> SendTestEmail(
        AdminSendTestEmailRequest req,
        NotificationEmailService emails)
    {
        if (string.IsNullOrWhiteSpace(req.ToEmail) || !req.ToEmail.Contains('@'))
            return Results.BadRequest(new ApiError("VALIDATION_FAILED", "A valid recipient email is required."));

        var result = await emails.SendTestEmailAsync(req.ToEmail.Trim());
        return Results.Ok(new AdminEmailSendResponse(result.Status, result.MessageId, result.Error));
    }

    private static async Task<IResult> SendCustomEmail(
        AdminSendEmailRequest req,
        IEmailService email)
    {
        if (string.IsNullOrWhiteSpace(req.ToEmail) || !req.ToEmail.Contains('@'))
            return Results.BadRequest(new ApiError("VALIDATION_FAILED", "A valid recipient email is required."));

        if (string.IsNullOrWhiteSpace(req.Subject))
            return Results.BadRequest(new ApiError("VALIDATION_FAILED", "Subject is required."));

        if (string.IsNullOrWhiteSpace(req.TextBody) && string.IsNullOrWhiteSpace(req.HtmlBody))
            return Results.BadRequest(new ApiError("VALIDATION_FAILED", "Text or HTML body is required."));

        var result = await email.SendAsync(
            req.ToEmail.Trim(),
            req.Subject.Trim(),
            req.TextBody,
            req.HtmlBody);

        return Results.Ok(new AdminEmailSendResponse(result.Status, result.MessageId, result.Error));
    }

    private static IResult GetEmailConfig(ConfigService config) =>
        Results.Ok(BuildEmailConfigResponse(config));

    private static async Task<IResult> UpdateEmailConfig(
        AdminUpdateEmailConfigRequest req,
        ConfigService config)
    {
        if (!string.IsNullOrWhiteSpace(req.Provider))
        {
            var provider = req.Provider.Trim().ToLowerInvariant();
            if (provider is not ("resend" or "disabled" or "none" or "noop"))
                return Results.BadRequest(new ApiError("VALIDATION_FAILED", "Provider must be resend, disabled, none, or noop."));

            await config.SetAsync("email.provider", provider);
        }

        if (!string.IsNullOrWhiteSpace(req.ResendApiKey))
            await config.SetAsync("Email:Resend:ApiKey", req.ResendApiKey.Trim());

        if (!string.IsNullOrWhiteSpace(req.FromEmail))
        {
            if (!req.FromEmail.Contains('@'))
                return Results.BadRequest(new ApiError("VALIDATION_FAILED", "FromEmail must be a valid email address."));

            await config.SetAsync("Email:Resend:From", req.FromEmail.Trim());
        }

        if (req.FromName is not null)
            await config.SetAsync("Email:Resend:FromName", req.FromName.Trim());

        if (!string.IsNullOrWhiteSpace(req.ReplyTo))
        {
            if (!req.ReplyTo.Contains('@'))
                return Results.BadRequest(new ApiError("VALIDATION_FAILED", "ReplyTo must be a valid email address."));

            await config.SetAsync("Email:ReplyTo", req.ReplyTo.Trim());
        }

        if (req.AdminRecipients is not null)
            await config.SetAsync("Email:AdminRecipients", req.AdminRecipients.Trim());

        if (req.SubjectPrefix is not null)
            await config.SetAsync("Email:SubjectPrefix", req.SubjectPrefix.Trim());

        if (!string.IsNullOrWhiteSpace(req.PublicBaseUrl))
            await config.SetAsync("App:PublicBaseUrl", req.PublicBaseUrl.Trim().TrimEnd('/'));

        return Results.Ok(BuildEmailConfigResponse(config));
    }

    private static AdminEmailConfigResponse BuildEmailConfigResponse(ConfigService config)
    {
        var apiKey = GetEmailConfigValue(config, "Email:Resend:ApiKey", "RESEND_API_KEY", "");
        return new AdminEmailConfigResponse(
            GetEmailConfigValue(config, "email.provider", "EMAIL_PROVIDER", "resend"),
            GetEmailConfigValue(config, "Email:Resend:From", "EMAIL_FROM", "no-reply@ryverental.info"),
            GetEmailConfigValue(config, "Email:Resend:FromName", "EMAIL_FROM_NAME", "RyveSwift"),
            GetEmailConfigValue(config, "Email:ReplyTo", "EMAIL_REPLY_TO", "support@ryvepool.com"),
            GetEmailConfigValue(config, "Email:AdminRecipients", "EMAIL_ADMIN_RECIPIENTS", ""),
            GetEmailConfigValue(config, "Email:SubjectPrefix", "EMAIL_SUBJECT_PREFIX", "RyveSwift"),
            GetEmailConfigValue(config, "App:PublicBaseUrl", "APP_PUBLIC_BASE_URL", "https://swift.ryvepos.com"),
            !string.IsNullOrWhiteSpace(apiKey) && !IsEmailPlaceholder(apiKey));
    }

    private static string GetEmailConfigValue(ConfigService config, string key, string envKey, string defaultValue)
    {
        var envValue = Environment.GetEnvironmentVariable(envKey)
            ?? Environment.GetEnvironmentVariable(key.Replace(":", "__"));
        return !string.IsNullOrWhiteSpace(envValue) ? envValue : config.Get(key, defaultValue);
    }

    private static bool IsEmailPlaceholder(string value) =>
        value.StartsWith("PLACEHOLDER_", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("CHANGE_ME", StringComparison.OrdinalIgnoreCase);

    private static AdminUserResponse MapAdminUser(User user) =>
        new(
            user.Id,
            user.Email,
            user.FullName,
            user.Phone,
            user.Role,
            user.IsSuspended,
            user.SuspendedAt,
            user.SuspendedReason,
            user.DeletedAt,
            user.PasswordResetRequired,
            user.EmailUnsubscribedAt,
            user.CreatedAt,
            user.LastLogin);

    private static IResult? ValidateUserMutation(
        string? email,
        string? password,
        string? role,
        bool isCreate)
    {
        if (isCreate && string.IsNullOrWhiteSpace(email))
            return Results.BadRequest(new ApiError("VALIDATION_FAILED", "Email is required."));

        if (!string.IsNullOrWhiteSpace(email) && !email.Contains('@'))
            return Results.BadRequest(new ApiError("VALIDATION_FAILED", "A valid email is required."));

        if (!string.IsNullOrWhiteSpace(password) && password.Length < 8)
            return Results.BadRequest(new ApiError("VALIDATION_FAILED", "Password must be at least 8 characters."));

        if (!string.IsNullOrWhiteSpace(role) &&
            !role.Equals("Admin", StringComparison.OrdinalIgnoreCase) &&
            !role.Equals("Customer", StringComparison.OrdinalIgnoreCase))
        {
            return Results.BadRequest(new ApiError("VALIDATION_FAILED", "Role must be Customer or Admin."));
        }

        return null;
    }

    private static string NormalizeEmail(string email) =>
        email.Trim().ToLowerInvariant();

    private static string NormalizeRole(string? role) =>
        string.IsNullOrWhiteSpace(role)
            ? "Customer"
            : role.Equals("Admin", StringComparison.OrdinalIgnoreCase)
                ? "Admin"
                : "Customer";

    private static async Task RevokeRefreshTokens(Guid userId, AppDbContext db)
    {
        var tokens = await db.UserRefreshTokens
            .Where(t => t.UserId == userId && !t.IsRevoked)
            .ToListAsync();
        tokens.ForEach(t => t.IsRevoked = true);
    }

    private static async Task<bool> HasAnotherActiveAdmin(AppDbContext db, Guid userId) =>
        await db.Users.AnyAsync(u =>
            u.Id != userId &&
            u.Role == "Admin" &&
            !u.IsSuspended &&
            !u.DeletedAt.HasValue);

    private static Guid? GetActorUserId(HttpContext ctx)
    {
        var sub = ctx.User.FindFirstValue(ClaimTypes.NameIdentifier)
               ?? ctx.User.FindFirstValue("sub");
        return Guid.TryParse(sub, out var id) ? id : null;
    }

    private static string GenerateTemporaryPassword()
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789!@#$%";
        return new string(Enumerable.Range(0, 16)
            .Select(_ => chars[RandomNumberGenerator.GetInt32(chars.Length)])
            .ToArray());
    }

    private static async Task<IResult> GetRevenueReport(
        AppDbContext db,
        HttpContext ctx)
    {
        var now = DateTime.UtcNow;
        var dateRangeError = TryResolveReportDateRange(ctx, now, out var fromDate, out var toDate);
        if (dateRangeError is not null) return dateRangeError;

        var periodShipments = await db.Shipments
            .Include(s => s.User)
            .Include(s => s.Packages)
            .AsNoTracking()
            .Where(s => s.CreatedAt >= fromDate && s.CreatedAt <= toDate)
            .ToListAsync();

        var quotes = await db.Quotes
            .AsNoTracking()
            .Where(q => q.CreatedAt >= fromDate && q.CreatedAt <= toDate)
            .ToListAsync();

        var payments = await db.Payments
            .AsNoTracking()
            .Where(p => p.CreatedAt >= fromDate && p.CreatedAt <= toDate)
            .ToListAsync();

        var events = await db.ShipmentEvents
            .AsNoTracking()
            .Where(e => e.CreatedAt >= fromDate && e.CreatedAt <= toDate)
            .ToListAsync();

        var newCustomerDates = await db.Users
            .AsNoTracking()
            .Where(u => u.Role == "Customer" && u.CreatedAt >= fromDate && u.CreatedAt <= toDate)
            .Select(u => u.CreatedAt)
            .ToListAsync();

        var totalCustomers = await db.Users
            .AsNoTracking()
            .CountAsync(u => u.Role == "Customer");

        var revenueFinancials = periodShipments
            .Where(s => RevenueStatuses.Contains(s.Status))
            .Select(BuildFinancials)
            .ToList();

        var totalRevenue = Money(revenueFinancials.Sum(f => f.Revenue));
        var dhlActuallyCharged = Money(revenueFinancials.Sum(f => f.DhlActuallyCharged));
        var markupRevenue = Money(revenueFinancials.Sum(f => f.MarkupRevenue));
        var platformFees = Money(revenueFinancials.Sum(f => f.PlatformFee));
        var grossProfit = Money(revenueFinancials.Sum(f => f.GrossProfit));
        var paidShipments = revenueFinancials.Count;
        var averageMarkupPercentApplied = Average(
            revenueFinancials.Where(f => f.MarkupPercentApplied.HasValue).Select(f => f.MarkupPercentApplied!.Value));

        var activeCustomerIds = periodShipments
            .Where(s => s.UserId.HasValue)
            .Select(s => s.UserId!.Value)
            .Distinct()
            .ToList();

        var repeatCustomers = revenueFinancials
            .Where(f => f.Shipment.UserId.HasValue)
            .GroupBy(f => f.Shipment.UserId!.Value)
            .Count(g => g.Count() > 1);

        var dhlBookingFailures = events.Count(e => e.EventType == "DhlBookingFailed");
        var periodWeight = Money(periodShipments.Sum(GetShipmentWeight));
        var labelDurations = GetLabelDurations(periodShipments, events);

        var response = new RevenueReportResponse(
            From: fromDate,
            To: toDate,
            Currency: "CAD",
            TotalRevenue: totalRevenue,
            DhlBaseCost: dhlActuallyCharged,
            MarkupEarned: grossProfit,
            TotalShipments: paidShipments,
            PaidShipments: paidShipments,
            RevenueSplit: new RevenueSplitResponse(
                CustomerRevenue: totalRevenue,
                DhlActuallyCharged: dhlActuallyCharged,
                MarkupRevenue: markupRevenue,
                PlatformFees: platformFees,
                GrossProfit: grossProfit,
                GrossMarginPercent: Percent(grossProfit, totalRevenue),
                AverageOrderValue: AverageMoney(totalRevenue, paidShipments),
                AverageDhlCharge: AverageMoney(dhlActuallyCharged, paidShipments),
                AverageMarkupRevenue: AverageMoney(markupRevenue, paidShipments),
                AveragePlatformFee: AverageMoney(platformFees, paidShipments),
                AverageGrossProfit: AverageMoney(grossProfit, paidShipments),
                AverageMarkupPercentApplied: averageMarkupPercentApplied,
                ShipmentsMissingDhlCharge: revenueFinancials.Count(f => !f.Shipment.DhlBaseRate.HasValue)),
            Operations: new AdminOperationalMetrics(
                ShipmentsCreated: periodShipments.Count,
                RevenueShipments: paidShipments,
                LabelsGenerated: periodShipments.Count(s => s.Status == "LabelGenerated"),
                InTransit: periodShipments.Count(s => s.Status == "InTransit"),
                Delivered: periodShipments.Count(s => s.Status == "Delivered"),
                PendingPayment: periodShipments.Count(s => s.Status == "PendingPayment"),
                Refunded: periodShipments.Count(s => s.Status == "Refunded"),
                Cancelled: periodShipments.Count(s => s.Status == "Cancelled"),
                Exceptions: periodShipments.Count(s => s.Status == "Exception"),
                DhlBookingFailures: dhlBookingFailures,
                DhlBookingFailureRatePercent: Percent(dhlBookingFailures, periodShipments.Count),
                TotalWeightKg: periodWeight,
                AverageWeightKg: AverageMoney(periodWeight, periodShipments.Count),
                AverageMinutesToLabel: labelDurations.Count == 0
                    ? null
                    : Math.Round((decimal)labelDurations.Average(ts => ts.TotalMinutes), 2, MidpointRounding.AwayFromZero)),
            Funnel: new AdminFunnelMetrics(
                QuotesCreated: quotes.Count,
                GuestQuotes: quotes.Count(q => !q.UserId.HasValue),
                RegisteredQuotes: quotes.Count(q => q.UserId.HasValue),
                ExpiredQuotes: quotes.Count(q => q.ExpiresAt < now),
                PaymentIntentsCreated: payments.Count,
                SucceededPayments: payments.Count(p => p.Status == "succeeded"),
                FailedPayments: payments.Count(p => p.Status == "failed"),
                PendingPayments: payments.Count(p => p.Status == "pending"),
                RefundedPayments: payments.Count(p => p.Status == "refunded"),
                QuoteToShipmentRatePercent: Percent(periodShipments.Count, quotes.Count),
                QuoteToPaidShipmentRatePercent: Percent(paidShipments, quotes.Count),
                PaymentSuccessRatePercent: Percent(payments.Count(p => p.Status == "succeeded"), payments.Count)),
            Customers: new AdminCustomerMetrics(
                TotalCustomers: totalCustomers,
                NewCustomers: newCustomerDates.Count,
                ActiveCustomers: activeCustomerIds.Count,
                RepeatCustomers: repeatCustomers,
                AverageRevenuePerActiveCustomer: AverageMoney(totalRevenue, activeCustomerIds.Count),
                AverageShipmentsPerActiveCustomer: AverageDecimal(periodShipments.Count, activeCustomerIds.Count)),
            TimeSeries: BuildTimeSeries(fromDate, toDate, revenueFinancials, periodShipments, quotes, newCustomerDates),
            TopRoutes: BuildTopRoutes(revenueFinancials),
            StatusBreakdown: BuildStatusBreakdown(periodShipments, revenueFinancials),
            ProductMix: BuildProductMix(revenueFinancials),
            TopCustomers: BuildTopCustomers(revenueFinancials));

        return Results.Ok(response);
    }

    private static ShipmentFinancials BuildFinancials(Shipment shipment)
    {
        var revenue = Money(shipment.TotalAmount);
        var dhlActuallyCharged = Money(shipment.DhlBaseRate ?? 0m);
        var platformFee = Money(shipment.PlatformFee ?? 0m);
        var markupRevenue = Money(revenue - dhlActuallyCharged - platformFee);
        var grossProfit = Money(revenue - dhlActuallyCharged);

        return new ShipmentFinancials(
            shipment,
            revenue,
            dhlActuallyCharged,
            markupRevenue,
            platformFee,
            grossProfit,
            Money(GetShipmentWeight(shipment)),
            shipment.MarkupPercent);
    }

    private static IReadOnlyList<AdminTimeSeriesPoint> BuildTimeSeries(
        DateTime fromDate,
        DateTime toDate,
        IReadOnlyList<ShipmentFinancials> financials,
        IReadOnlyList<Shipment> periodShipments,
        IReadOnlyList<Quote> quotes,
        IReadOnlyList<DateTime> newCustomerDates)
    {
        var monthly = (toDate.Date - fromDate.Date).TotalDays > 92;
        Func<DateTime, DateTime> keySelector = monthly ? MonthKey : DayKey;
        Func<DateTime, DateTime> increment = monthly ? d => d.AddMonths(1) : d => d.AddDays(1);
        var periodFormat = monthly ? "yyyy-MM" : "yyyy-MM-dd";

        var financialsByPeriod = financials
            .GroupBy(f => keySelector(f.Shipment.CreatedAt))
            .ToDictionary(g => g.Key, g => g.ToList());

        var shipmentsByPeriod = periodShipments
            .GroupBy(s => keySelector(s.CreatedAt))
            .ToDictionary(g => g.Key, g => g.Count());

        var quotesByPeriod = quotes
            .GroupBy(q => keySelector(q.CreatedAt))
            .ToDictionary(g => g.Key, g => g.Count());

        var newCustomersByPeriod = newCustomerDates
            .GroupBy(keySelector)
            .ToDictionary(g => g.Key, g => g.Count());

        var points = new List<AdminTimeSeriesPoint>();
        for (var cursor = keySelector(fromDate); cursor <= keySelector(toDate); cursor = increment(cursor))
        {
            financialsByPeriod.TryGetValue(cursor, out var periodFinancials);
            periodFinancials ??= new List<ShipmentFinancials>();

            var revenue = Money(periodFinancials.Sum(f => f.Revenue));
            var dhlActuallyCharged = Money(periodFinancials.Sum(f => f.DhlActuallyCharged));
            var markupRevenue = Money(periodFinancials.Sum(f => f.MarkupRevenue));
            var platformFees = Money(periodFinancials.Sum(f => f.PlatformFee));
            var grossProfit = Money(periodFinancials.Sum(f => f.GrossProfit));

            points.Add(new AdminTimeSeriesPoint(
                PeriodStart: cursor,
                Period: cursor.ToString(periodFormat, CultureInfo.InvariantCulture),
                Quotes: quotesByPeriod.GetValueOrDefault(cursor),
                Shipments: shipmentsByPeriod.GetValueOrDefault(cursor),
                PaidShipments: periodFinancials.Count,
                NewCustomers: newCustomersByPeriod.GetValueOrDefault(cursor),
                Revenue: revenue,
                DhlActuallyCharged: dhlActuallyCharged,
                MarkupRevenue: markupRevenue,
                PlatformFees: platformFees,
                GrossProfit: grossProfit));
        }

        return points;
    }

    private static IReadOnlyList<AdminRouteMetric> BuildTopRoutes(IReadOnlyList<ShipmentFinancials> financials)
    {
        return financials
            .GroupBy(f => new
            {
                Origin = f.Shipment.OriginCountry,
                Destination = f.Shipment.DestinationCountry
            })
            .Select(g =>
            {
                var items = g.ToList();
                var revenue = Money(items.Sum(f => f.Revenue));
                var dhlActuallyCharged = Money(items.Sum(f => f.DhlActuallyCharged));
                var markupRevenue = Money(items.Sum(f => f.MarkupRevenue));
                var platformFees = Money(items.Sum(f => f.PlatformFee));
                var grossProfit = Money(items.Sum(f => f.GrossProfit));
                var weight = Money(items.Sum(f => f.WeightKg));

                return new AdminRouteMetric(
                    Route: $"{g.Key.Origin}-{g.Key.Destination}",
                    OriginCountry: g.Key.Origin,
                    DestinationCountry: g.Key.Destination,
                    Shipments: items.Count,
                    Revenue: revenue,
                    DhlActuallyCharged: dhlActuallyCharged,
                    MarkupRevenue: markupRevenue,
                    PlatformFees: platformFees,
                    GrossProfit: grossProfit,
                    GrossMarginPercent: Percent(grossProfit, revenue),
                    AverageRevenue: AverageMoney(revenue, items.Count),
                    AverageWeightKg: AverageMoney(weight, items.Count));
            })
            .OrderByDescending(m => m.Revenue)
            .ThenByDescending(m => m.Shipments)
            .Take(10)
            .ToList();
    }

    private static IReadOnlyList<AdminStatusMetric> BuildStatusBreakdown(
        IReadOnlyList<Shipment> periodShipments,
        IReadOnlyList<ShipmentFinancials> financials)
    {
        var revenueByStatus = financials
            .GroupBy(f => f.Shipment.Status)
            .ToDictionary(
                g => g.Key,
                g => Money(g.Sum(f => f.Revenue)),
                StringComparer.OrdinalIgnoreCase);

        return periodShipments
            .GroupBy(s => s.Status)
            .Select(g => new AdminStatusMetric(
                Status: g.Key,
                Count: g.Count(),
                Revenue: revenueByStatus.GetValueOrDefault(g.Key)))
            .OrderByDescending(m => m.Count)
            .ThenBy(m => m.Status)
            .ToList();
    }

    private static IReadOnlyList<AdminProductMetric> BuildProductMix(IReadOnlyList<ShipmentFinancials> financials)
    {
        return financials
            .GroupBy(f => f.Shipment.ProductCode)
            .Select(g =>
            {
                var items = g.ToList();
                var revenue = Money(items.Sum(f => f.Revenue));
                var grossProfit = Money(items.Sum(f => f.GrossProfit));

                return new AdminProductMetric(
                    ProductCode: g.Key,
                    Service: GetServiceName(g.Key),
                    Shipments: items.Count,
                    Revenue: revenue,
                    GrossProfit: grossProfit,
                    AverageRevenue: AverageMoney(revenue, items.Count));
            })
            .OrderByDescending(m => m.Revenue)
            .ToList();
    }

    private static IReadOnlyList<AdminTopCustomerMetric> BuildTopCustomers(IReadOnlyList<ShipmentFinancials> financials)
    {
        return financials
            .Where(f => f.Shipment.UserId.HasValue)
            .GroupBy(f => f.Shipment.UserId!.Value)
            .Select(g =>
            {
                var items = g.ToList();
                var revenue = Money(items.Sum(f => f.Revenue));
                var grossProfit = Money(items.Sum(f => f.GrossProfit));

                return new AdminTopCustomerMetric(
                    UserId: g.Key,
                    Email: items.Select(f => f.Shipment.User?.Email).FirstOrDefault(email => !string.IsNullOrWhiteSpace(email)),
                    Shipments: items.Count,
                    Revenue: revenue,
                    GrossProfit: grossProfit,
                    LastShipmentAt: items.Max(f => f.Shipment.CreatedAt));
            })
            .OrderByDescending(m => m.Revenue)
            .ThenByDescending(m => m.Shipments)
            .Take(10)
            .ToList();
    }

    private static IReadOnlyList<TimeSpan> GetLabelDurations(
        IReadOnlyList<Shipment> shipments,
        IReadOnlyList<ShipmentEvent> events)
    {
        var shipmentCreatedAt = shipments.ToDictionary(s => s.Id, s => s.CreatedAt);

        return events
            .Where(e => e.EventType == "LabelGenerated")
            .GroupBy(e => e.ShipmentId)
            .Select(g => new { ShipmentId = g.Key, LabelCreatedAt = g.Min(e => e.CreatedAt) })
            .Where(x => shipmentCreatedAt.ContainsKey(x.ShipmentId) &&
                        x.LabelCreatedAt >= shipmentCreatedAt[x.ShipmentId])
            .Select(x => x.LabelCreatedAt - shipmentCreatedAt[x.ShipmentId])
            .ToList();
    }

    private static IResult? TryResolveReportDateRange(
        HttpContext ctx,
        DateTime now,
        out DateTime fromDate,
        out DateTime toDate)
    {
        fromDate = now.AddMonths(-1);
        toDate = now;

        var fromValue = GetFirstQueryValue(ctx, "from", "fromDate", "startDate");
        if (!string.IsNullOrWhiteSpace(fromValue) &&
            !TryParseReportDate(fromValue, isEndDate: false, out fromDate))
        {
            return Results.BadRequest(new ApiError(
                "VALIDATION_FAILED",
                "'from' must be a valid date or date-time."));
        }

        var toValue = GetFirstQueryValue(ctx, "to", "toDate", "endDate");
        if (!string.IsNullOrWhiteSpace(toValue) &&
            !TryParseReportDate(toValue, isEndDate: true, out toDate))
        {
            return Results.BadRequest(new ApiError(
                "VALIDATION_FAILED",
                "'to' must be a valid date or date-time."));
        }

        if (toDate < fromDate)
        {
            return Results.BadRequest(new ApiError(
                "VALIDATION_FAILED",
                "'to' must be greater than or equal to 'from'."));
        }

        return null;
    }

    private static string? GetFirstQueryValue(HttpContext ctx, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (ctx.Request.Query.TryGetValue(key, out var value) &&
                !string.IsNullOrWhiteSpace(value.FirstOrDefault()))
            {
                return value.First()!;
            }
        }

        return null;
    }

    private static bool TryParseReportDate(string value, bool isEndDate, out DateTime result)
    {
        if (DateOnly.TryParseExact(
                value,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var dateOnly))
        {
            result = DateTime.SpecifyKind(dateOnly.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
            if (isEndDate) result = result.AddDays(1).AddTicks(-1);
            return true;
        }

        if (DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed))
        {
            result = parsed.UtcDateTime;
            return true;
        }

        result = default;
        return false;
    }

    private static DateTime DayKey(DateTime value) =>
        DateTime.SpecifyKind(value.Date, DateTimeKind.Utc);

    private static DateTime MonthKey(DateTime value) =>
        DateTime.SpecifyKind(new DateTime(value.Year, value.Month, 1), DateTimeKind.Utc);

    private static decimal GetShipmentWeight(Shipment shipment) =>
        shipment.Packages.Sum(p => p.WeightKg);

    private static string GetServiceName(string productCode)
    {
        if (DhlProductPolicy.TryGetHiddenServiceName(productCode, null, out _))
            return "DHL service unavailable";

        return "DHL Express Worldwide";
    }

    private static decimal Money(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);

    private static decimal AverageMoney(decimal total, int count) =>
        count == 0 ? 0m : Money(total / count);

    private static decimal AverageDecimal(decimal total, int count) =>
        count == 0 ? 0m : Math.Round(total / count, 2, MidpointRounding.AwayFromZero);

    private static decimal Average(IEnumerable<decimal> values)
    {
        var list = values.ToList();
        return list.Count == 0 ? 0m : Math.Round(list.Average(), 2, MidpointRounding.AwayFromZero);
    }

    private static decimal Percent(decimal numerator, decimal denominator) =>
        denominator == 0m ? 0m : Math.Round(numerator / denominator * 100m, 2, MidpointRounding.AwayFromZero);

    private static decimal Percent(int numerator, int denominator) =>
        Percent((decimal)numerator, denominator);

    private sealed record ShipmentFinancials(
        Shipment Shipment,
        decimal Revenue,
        decimal DhlActuallyCharged,
        decimal MarkupRevenue,
        decimal PlatformFee,
        decimal GrossProfit,
        decimal WeightKg,
        decimal? MarkupPercentApplied);

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

        var productCode = NormalizeMarkupProductCode(req.ProductCode);
        if (productCode.Error is not null)
            return productCode.Error;

        var rule = new MarkupRule
        {
            OriginCountry = req.OriginCountry?.ToUpper(),
            DestinationCountry = req.DestinationCountry?.ToUpper(),
            MinWeightKg = req.MinWeightKg,
            MaxWeightKg = req.MaxWeightKg,
            ProductCode = productCode.Value,
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

        var productCode = NormalizeMarkupProductCode(req.ProductCode);
        if (productCode.Error is not null)
            return productCode.Error;

        rule.OriginCountry = req.OriginCountry?.ToUpper();
        rule.DestinationCountry = req.DestinationCountry?.ToUpper();
        rule.MinWeightKg = req.MinWeightKg;
        rule.MaxWeightKg = req.MaxWeightKg;
        rule.ProductCode = productCode.Value;
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

    private static (string? Value, IResult? Error) NormalizeMarkupProductCode(string? productCode)
    {
        if (string.IsNullOrWhiteSpace(productCode))
            return (null, null);

        var normalized = productCode.Trim().ToUpperInvariant();
        if (normalized == "P")
            return (normalized, null);

        var message = DhlProductPolicy.TryGetHiddenServiceName(normalized, null, out var serviceName)
            ? DhlProductPolicy.HiddenServiceMessage(serviceName)
            : "Only DHL Express Worldwide product code P is supported by this certified integration.";

        return (null, Results.BadRequest(new ApiError("VALIDATION_FAILED", message)));
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
