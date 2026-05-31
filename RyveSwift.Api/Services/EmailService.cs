using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using RyveSwift.Api.Data;
using RyveSwift.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace RyveSwift.Api.Services;

public record EmailSendResult(string Status, string? MessageId = null, string? Error = null);

public interface IEmailService
{
    Task<EmailSendResult> SendAsync(
        string toEmail,
        string subject,
        string? textBody,
        string? htmlBody,
        string? fromEmail = null,
        string? fromName = null,
        CancellationToken ct = default);
}

public class EmailDispatcher : IEmailService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ConfigService _config;
    private readonly ILogger<EmailDispatcher> _logger;

    public EmailDispatcher(
        IHttpClientFactory httpClientFactory,
        ConfigService config,
        ILogger<EmailDispatcher> logger)
    {
        _httpClientFactory = httpClientFactory;
        _config = config;
        _logger = logger;
    }

    public async Task<EmailSendResult> SendAsync(
        string toEmail,
        string subject,
        string? textBody,
        string? htmlBody,
        string? fromEmail = null,
        string? fromName = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(toEmail))
            return new EmailSendResult("skipped", Error: "missing_recipient");

        var provider = GetConfig("email.provider", "EMAIL_PROVIDER", "resend").Trim().ToLowerInvariant();
        if (provider is "disabled" or "none" or "noop")
            return new EmailSendResult("skipped", Error: "email_disabled");

        if (provider != "resend")
            return new EmailSendResult("skipped", Error: $"unsupported_provider:{provider}");

        return await SendWithResendAsync(toEmail, subject, textBody, htmlBody, fromEmail, fromName, ct);
    }

    private async Task<EmailSendResult> SendWithResendAsync(
        string toEmail,
        string subject,
        string? textBody,
        string? htmlBody,
        string? fromEmail,
        string? fromName,
        CancellationToken ct)
    {
        var apiKey = GetConfig("Email:Resend:ApiKey", "RESEND_API_KEY", "");
        if (string.IsNullOrWhiteSpace(apiKey) || IsPlaceholder(apiKey))
        {
            _logger.LogWarning("Email skipped because Resend API key is not configured.");
            return new EmailSendResult("skipped", Error: "missing_resend_api_key");
        }

        var resolvedFromEmail = fromEmail ?? GetConfig("Email:Resend:From", "EMAIL_FROM", "no-reply@ryverental.info");
        var resolvedFromName = fromName ?? GetConfig("Email:Resend:FromName", "EMAIL_FROM_NAME", "RyveSwift");
        var replyTo = GetConfig("Email:ReplyTo", "EMAIL_REPLY_TO", "support@ryvepool.com");
        var brandedSubject = ApplySubjectBranding(subject);
        var from = string.IsNullOrWhiteSpace(resolvedFromName)
            ? resolvedFromEmail
            : $"{resolvedFromName} <{resolvedFromEmail}>";

        var payload = new
        {
            from,
            to = new[] { toEmail },
            subject = brandedSubject,
            text = textBody,
            html = htmlBody,
            reply_to = replyTo
        };

        try
        {
            var client = _httpClientFactory.CreateClient("resend");
            using var request = new HttpRequestMessage(HttpMethod.Post, "emails")
            {
                Content = JsonContent.Create(payload)
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            using var response = await client.SendAsync(request, ct);
            var responseBody = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Resend email failed with {Status}: {Body}", response.StatusCode, responseBody);
                return new EmailSendResult("failed", Error: $"resend_error:{(int)response.StatusCode}");
            }

            string? messageId = null;
            try
            {
                using var doc = JsonDocument.Parse(responseBody);
                if (doc.RootElement.TryGetProperty("id", out var id))
                    messageId = id.GetString();
            }
            catch (JsonException)
            {
                // Resend success without parseable JSON is still a sent email.
            }

            return new EmailSendResult("sent", messageId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Resend email send failed.");
            return new EmailSendResult("failed", Error: "resend_exception");
        }
    }

    private string GetConfig(string key, string envKey, string defaultValue)
    {
        var envValue = Environment.GetEnvironmentVariable(envKey)
            ?? Environment.GetEnvironmentVariable(key.Replace(":", "__"));
        return !string.IsNullOrWhiteSpace(envValue) ? envValue : _config.Get(key, defaultValue);
    }

    private string ApplySubjectBranding(string subject)
    {
        var prefix = GetConfig("Email:SubjectPrefix", "EMAIL_SUBJECT_PREFIX", "RyveSwift").Trim();
        if (string.IsNullOrWhiteSpace(prefix)) return subject;
        if (subject.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return subject;
        return $"{prefix}: {subject}";
    }

    private static bool IsPlaceholder(string value) =>
        value.StartsWith("PLACEHOLDER_", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("CHANGE_ME", StringComparison.OrdinalIgnoreCase);
}

public class NotificationEmailService
{
    private readonly IEmailService _email;
    private readonly ConfigService _config;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly EmailPreferenceTokenService _emailPreferenceTokens;
    private readonly ILogger<NotificationEmailService> _logger;

    public NotificationEmailService(
        IEmailService email,
        ConfigService config,
        IServiceScopeFactory scopeFactory,
        EmailPreferenceTokenService emailPreferenceTokens,
        ILogger<NotificationEmailService> logger)
    {
        _email = email;
        _config = config;
        _scopeFactory = scopeFactory;
        _emailPreferenceTokens = emailPreferenceTokens;
        _logger = logger;
    }

    public Task<EmailSendResult> SendTestEmailAsync(string toEmail, CancellationToken ct = default) =>
        SafeSendUserEmail(
            toEmail,
            "RyveSwift email test",
            Template("Email test", "<p>Your RyveSwift email configuration is working.</p>"),
            ct);

    public Task SendWelcomeEmailAsync(User user, CancellationToken ct = default) =>
        SafeSendUserEmail(
            user,
            "Welcome to RyveSwift",
            Template("Welcome to RyveSwift", $"""
                <p>Hi {H(user.FullName ?? user.Email)},</p>
                <p>Your RyveSwift account is ready.</p>
                <p>You can now create quotes, book DHL shipments, and track deliveries from your account.</p>
                """),
            ct);

    public Task SendAdminUserCreatedEmailAsync(User user, string? temporaryPassword, CancellationToken ct = default)
    {
        var passwordBlock = string.IsNullOrWhiteSpace(temporaryPassword)
            ? "<p>An admin created your account. Use the password they provided to sign in.</p>"
            : $"<p>Your temporary password is:</p><p><strong>{H(temporaryPassword)}</strong></p><p>Please change it after signing in.</p>";

        return SafeSendUserEmail(
            user,
            "Your RyveSwift account was created",
            Template("Your RyveSwift account was created", $"""
                <p>Hi {H(user.FullName ?? user.Email)},</p>
                {passwordBlock}
                """),
            ct,
            respectUnsubscribe: false);
    }

    public Task SendPasswordResetEmailAsync(User user, string? temporaryPassword, bool resetRequired, CancellationToken ct = default)
    {
        var passwordBlock = string.IsNullOrWhiteSpace(temporaryPassword)
            ? "<p>An admin has reset your password to a new value they will provide separately.</p>"
            : $"<p>Your temporary password is:</p><p><strong>{H(temporaryPassword)}</strong></p>";
        var resetBlock = resetRequired ? "<p>You will be asked to change this password after signing in.</p>" : "";

        return SafeSendUserEmail(
            user,
            "Your RyveSwift password was reset",
            Template("Your RyveSwift password was reset", $"""
                <p>Hi {H(user.FullName ?? user.Email)},</p>
                {passwordBlock}
                {resetBlock}
                <p>If you did not request this, contact support immediately.</p>
                """),
            ct,
            respectUnsubscribe: false);
    }

    public Task SendPasswordResetLinkEmailAsync(User user, string resetUrl, int expiresMinutes, CancellationToken ct = default) =>
        SafeSendUserEmail(
            user,
            "Reset your RyveSwift password",
            Template("Reset your password", $"""
                <p>Hi {H(user.FullName ?? user.Email)},</p>
                <p>Use the link below to reset your RyveSwift password. This link expires in {expiresMinutes} minutes.</p>
                <p><a href="{H(resetUrl)}">Reset password</a></p>
                <p>If you did not request this, you can ignore this email.</p>
                """),
            ct,
            respectUnsubscribe: false);

    public Task SendPasswordChangedEmailAsync(User user, CancellationToken ct = default) =>
        SafeSendUserEmail(
            user,
            "Your RyveSwift password changed",
            Template("Your RyveSwift password changed", $"""
                <p>Hi {H(user.FullName ?? user.Email)},</p>
                <p>Your password was changed successfully.</p>
                <p>If this was not you, contact support immediately.</p>
                """),
            ct,
            respectUnsubscribe: false);

    public Task SendAccountSuspendedEmailAsync(User user, string? reason, CancellationToken ct = default) =>
        SafeSendUserEmail(
            user,
            "Your RyveSwift account is suspended",
            Template("Account suspended", $"""
                <p>Hi {H(user.FullName ?? user.Email)},</p>
                <p>Your RyveSwift account has been suspended.</p>
                {(string.IsNullOrWhiteSpace(reason) ? "" : $"<p>Reason: {H(reason)}</p>")}
                <p>Contact support if you believe this needs review.</p>
                """),
            ct,
            respectUnsubscribe: false);

    public Task SendAccountReactivatedEmailAsync(User user, CancellationToken ct = default) =>
        SafeSendUserEmail(
            user,
            "Your RyveSwift account is active again",
            Template("Account reactivated", $"""
                <p>Hi {H(user.FullName ?? user.Email)},</p>
                <p>Your RyveSwift account has been reactivated.</p>
                """),
            ct,
            respectUnsubscribe: false);

    public Task SendAccountDeletedEmailAsync(User user, CancellationToken ct = default) =>
        SafeSendUserEmail(
            user,
            "Your RyveSwift account was deleted",
            Template("Account deleted", $"""
                <p>Hi {H(user.FullName ?? user.Email)},</p>
                <p>Your RyveSwift account has been deleted by an admin.</p>
                <p>Shipment and payment records are retained for operational and compliance purposes.</p>
                """),
            ct,
            respectUnsubscribe: false);

    public async Task SendNewUserAdminAlertAsync(User user, CancellationToken ct = default)
    {
        await SafeNotifyAdmins(
            "New RyveSwift user registered",
            Template("New user registered", $"""
                <p>A new user registered.</p>
                <p><strong>Email:</strong> {H(user.Email)}</p>
                <p><strong>Name:</strong> {H(user.FullName ?? "n/a")}</p>
                """),
            ct);
    }

    public async Task SendShipmentLabelCreatedAsync(Shipment shipment, User? user, CancellationToken ct = default)
    {
        if (user is not null)
        {
            await SafeSendUserEmail(
                user,
                "Your DHL label is ready",
                Template("Your DHL label is ready", $"""
                    <p>Your shipment from {H(shipment.OriginCountry)} to {H(shipment.DestinationCountry)} is booked.</p>
                    <p><strong>Tracking number:</strong> {H(shipment.TrackingNumber ?? "pending")}</p>
                    <p><strong>Total:</strong> {Money(shipment.TotalAmount)} {H(shipment.Currency)}</p>
                    """),
                ct);
        }

        await SafeNotifyAdmins(
            "New RyveSwift shipment booked",
            Template("New shipment booked", $"""
                <p>A shipment label was generated.</p>
                <p><strong>Shipment:</strong> {shipment.Id}</p>
                <p><strong>User:</strong> {H(user?.Email ?? "unknown")}</p>
                <p><strong>Route:</strong> {H(shipment.OriginCountry)} to {H(shipment.DestinationCountry)}</p>
                <p><strong>Tracking:</strong> {H(shipment.TrackingNumber ?? "pending")}</p>
                <p><strong>Revenue:</strong> {Money(shipment.TotalAmount)} {H(shipment.Currency)}</p>
                """),
            ct);
    }

    public async Task SendBookingFailureAsync(Shipment shipment, User? user, string reason, string? refundId, CancellationToken ct = default)
    {
        if (user is not null)
        {
            await SafeSendUserEmail(
                user,
                "Your RyveSwift booking could not be completed",
                Template("Booking could not be completed", $"""
                    <p>Your shipment could not be booked with DHL.</p>
                    <p><strong>Shipment:</strong> {shipment.Id}</p>
                    <p><strong>Reason:</strong> {H(reason)}</p>
                    {(string.IsNullOrWhiteSpace(refundId) ? "" : $"<p><strong>Refund:</strong> {H(refundId)}</p>")}
                    """),
                ct);
        }

        await SafeNotifyAdmins(
            "DHL booking failure",
            Template("DHL booking failure", $"""
                <p>DHL booking failed.</p>
                <p><strong>Shipment:</strong> {shipment.Id}</p>
                <p><strong>User:</strong> {H(user?.Email ?? "unknown")}</p>
                <p><strong>Reason:</strong> {H(reason)}</p>
                {(string.IsNullOrWhiteSpace(refundId) ? "" : $"<p><strong>Refund:</strong> {H(refundId)}</p>")}
                """),
            ct);
    }

    public Task SendDhlTransientFailureAdminAlertAsync(Shipment shipment, User? user, string reason, CancellationToken ct = default) =>
        SafeNotifyAdmins(
            "DHL transient booking error",
            Template("DHL transient booking error", $"""
                <p>DHL booking returned a transient error. Payment is preserved and booking can be retried.</p>
                <p><strong>Shipment:</strong> {shipment.Id}</p>
                <p><strong>User:</strong> {H(user?.Email ?? "unknown")}</p>
                <p><strong>Reason:</strong> {H(reason)}</p>
                """),
            ct);

    public async Task SendShipmentStatusChangedAsync(Shipment shipment, User? user, string oldStatus, string newStatus, CancellationToken ct = default)
    {
        if (newStatus is not ("Delivered" or "Exception")) return;

        if (user is not null)
        {
            await SafeSendUserEmail(
                user,
                newStatus == "Delivered" ? "Your shipment was delivered" : "Your shipment needs attention",
                Template(newStatus == "Delivered" ? "Shipment delivered" : "Shipment exception", $"""
                    <p>Your shipment status changed from {H(oldStatus)} to {H(newStatus)}.</p>
                    <p><strong>Tracking number:</strong> {H(shipment.TrackingNumber ?? "n/a")}</p>
                    """),
                ct);
        }

        if (newStatus == "Exception")
        {
            await SafeNotifyAdmins(
                "Shipment exception",
                Template("Shipment exception", $"""
                    <p>A shipment moved into exception status.</p>
                    <p><strong>Shipment:</strong> {shipment.Id}</p>
                    <p><strong>User:</strong> {H(user?.Email ?? "unknown")}</p>
                    <p><strong>Tracking:</strong> {H(shipment.TrackingNumber ?? "n/a")}</p>
                    """),
                ct);
        }
    }

    public async Task SendPaymentFailedAsync(Payment payment, Shipment? shipment, User? user, CancellationToken ct = default)
    {
        if (user is not null)
        {
            await SafeSendUserEmail(
                user,
                "RyveSwift payment failed",
                Template("Payment failed", $"""
                    <p>Your payment could not be completed.</p>
                    <p><strong>Amount:</strong> {Money(payment.Amount)} {H(payment.Currency)}</p>
                    """),
                ct);
        }

        await SafeNotifyAdmins(
            "RyveSwift payment failed",
            Template("Payment failed", $"""
                <p>A payment failed.</p>
                <p><strong>PaymentIntent:</strong> {H(payment.StripePaymentIntentId)}</p>
                <p><strong>User:</strong> {H(user?.Email ?? "unknown")}</p>
                <p><strong>Shipment:</strong> {shipment?.Id.ToString() ?? "n/a"}</p>
                """),
            ct);
    }

    public async Task SendRefundedAsync(Payment payment, Shipment? shipment, User? user, CancellationToken ct = default)
    {
        if (user is not null)
        {
            await SafeSendUserEmail(
                user,
                "RyveSwift refund processed",
                Template("Refund processed", $"""
                    <p>A refund was processed for your RyveSwift shipment.</p>
                    <p><strong>Amount:</strong> {Money(payment.Amount)} {H(payment.Currency)}</p>
                    """),
                ct);
        }

        await SafeNotifyAdmins(
            "RyveSwift refund processed",
            Template("Refund processed", $"""
                <p>A refund was processed.</p>
                <p><strong>PaymentIntent:</strong> {H(payment.StripePaymentIntentId)}</p>
                <p><strong>User:</strong> {H(user?.Email ?? "unknown")}</p>
                <p><strong>Shipment:</strong> {shipment?.Id.ToString() ?? "n/a"}</p>
                """),
            ct);
    }

    public Task SendDisputeAdminAlertAsync(Shipment? shipment, User? user, string disputeId, string reason, long amount, CancellationToken ct = default) =>
        SafeNotifyAdmins(
            "Stripe dispute opened",
            Template("Stripe dispute opened", $"""
                <p>A Stripe dispute was opened.</p>
                <p><strong>Dispute:</strong> {H(disputeId)}</p>
                <p><strong>Reason:</strong> {H(reason)}</p>
                <p><strong>Amount:</strong> {amount}</p>
                <p><strong>User:</strong> {H(user?.Email ?? "unknown")}</p>
                <p><strong>Shipment:</strong> {shipment?.Id.ToString() ?? "n/a"}</p>
                """),
            ct);

    public async Task SendShipmentCancelledAsync(Shipment shipment, User? user, CancellationToken ct = default)
    {
        if (user is not null)
        {
            await SafeSendUserEmail(
                user,
                "Your RyveSwift shipment was cancelled",
                Template("Shipment cancelled", $"""
                    <p>Your shipment has been cancelled.</p>
                    <p><strong>Shipment:</strong> {shipment.Id}</p>
                    <p><strong>Tracking:</strong> {H(shipment.TrackingNumber ?? "n/a")}</p>
                    """),
                ct);
        }

        await SafeNotifyAdmins(
            "Shipment cancelled",
            Template("Shipment cancelled", $"""
                <p>A customer cancelled a shipment.</p>
                <p><strong>Shipment:</strong> {shipment.Id}</p>
                <p><strong>User:</strong> {H(user?.Email ?? "unknown")}</p>
                """),
            ct);
    }

    private async Task<EmailSendResult> SafeSendUserEmail(
        User user,
        string subject,
        string html,
        CancellationToken ct = default,
        bool respectUnsubscribe = true)
    {
        if (respectUnsubscribe && user.EmailUnsubscribedAt.HasValue)
        {
            _logger.LogInformation("Email skipped because {Recipient} is unsubscribed: {Subject}", user.Email, subject);
            return new EmailSendResult("skipped", Error: "email_unsubscribed");
        }

        return await SafeSendUserEmail(user.Email, subject, AddEmailPreferenceFooter(html, user), ct);
    }

    private async Task<EmailSendResult> SafeSendUserEmail(
        string toEmail,
        string subject,
        string html,
        CancellationToken ct = default)
    {
        try
        {
            return await _email.SendAsync(toEmail, subject, null, html, ct: ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "User email failed for {Recipient}: {Subject}", toEmail, subject);
            return new EmailSendResult("failed", Error: "notification_exception");
        }
    }

    private string AddEmailPreferenceFooter(string html, User user)
    {
        var token = _emailPreferenceTokens.CreateToken(user.Id);
        var publicBaseUrl = GetConfig("App:PublicBaseUrl", "APP_PUBLIC_BASE_URL",
            _config.Get("App:FrontendBaseUrl", "https://swift.ryvepos.com")).TrimEnd('/');
        var unsubscribeUrl = $"{publicBaseUrl}/api/email/unsubscribe?token={Uri.EscapeDataString(token)}";
        var resubscribeUrl = $"{publicBaseUrl}/api/email/resubscribe?token={Uri.EscapeDataString(token)}";
        var actionUrl = user.EmailUnsubscribedAt.HasValue ? resubscribeUrl : unsubscribeUrl;
        var actionText = user.EmailUnsubscribedAt.HasValue ? "Resubscribe" : "Unsubscribe";

        var footer = $"""
            <p style="font-size: 12px; color: #6b7280;">
              <a href="{H(actionUrl)}" style="color: #374151;">{actionText}</a> from non-essential RyveSwift email notifications.
              Security and account access emails may still be sent.
            </p>
            """;

        return html.Replace("</div>", $"{footer}</div>", StringComparison.OrdinalIgnoreCase);
    }

    private string GetConfig(string key, string envKey, string defaultValue)
    {
        var envValue = Environment.GetEnvironmentVariable(envKey)
            ?? Environment.GetEnvironmentVariable(key.Replace(":", "__"));
        return !string.IsNullOrWhiteSpace(envValue) ? envValue : _config.Get(key, defaultValue);
    }

    private async Task SafeNotifyAdmins(string subject, string html, CancellationToken ct = default)
    {
        var recipients = await GetAdminRecipientsAsync(ct);
        foreach (var recipient in recipients)
            await SafeSendUserEmail(recipient, subject, html, ct);
    }

    private async Task<IReadOnlyList<string>> GetAdminRecipientsAsync(CancellationToken ct)
    {
        var configured = Environment.GetEnvironmentVariable("EMAIL_ADMIN_RECIPIENTS")
            ?? Environment.GetEnvironmentVariable("Email__AdminRecipients")
            ?? _config.Get("Email:AdminRecipients", "");
        var recipients = configured
            .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => x.Contains('@'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (recipients.Count > 0) return recipients;

        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Users
            .AsNoTracking()
            .Where(u => u.Role == "Admin" && !u.IsSuspended && !u.DeletedAt.HasValue)
            .Select(u => u.Email)
            .ToListAsync(ct);
    }

    private static string Template(string title, string body) =>
        $$"""
        <!doctype html>
        <html>
        <body style="font-family: Arial, sans-serif; color: #111827; line-height: 1.5;">
          <div style="max-width: 640px; margin: 0 auto; padding: 24px;">
            <h1 style="font-size: 22px; margin-bottom: 16px;">{{H(title)}}</h1>
            {{body}}
            <hr style="border: 0; border-top: 1px solid #e5e7eb; margin: 24px 0;" />
            <p style="font-size: 12px; color: #6b7280;">RyveSwift Logistics</p>
          </div>
        </body>
        </html>
        """;

    private static string H(string value) => WebUtility.HtmlEncode(value);

    private static string Money(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero).ToString("0.00");
}
