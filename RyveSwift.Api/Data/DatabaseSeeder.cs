using Microsoft.EntityFrameworkCore;
using RyveSwift.Api.Entities;

namespace RyveSwift.Api.Data;

public static class DatabaseSeeder
{
    // Configs that are upserted on every startup: if the row is missing it is inserted;
    // if it exists with a placeholder value it is updated to the real value.
    private static readonly List<AppConfig> DefaultConfigs = new()
    {
        // JWT
        new() { Key = "JWT_SECRET", Value = "CHANGE_THIS_TO_A_STRONG_SECRET_AT_LEAST_32_CHARS", Description = "JWT signing secret — must be >= 32 characters", IsSecret = true },
        new() { Key = "JWT_ISSUER", Value = "afriswift-api", Description = "JWT issuer claim" },
        new() { Key = "JWT_AUDIENCE", Value = "afriswift-clients", Description = "JWT audience claim" },
        new() { Key = "JWT_EXPIRY_MINUTES", Value = "60", Description = "Access token lifetime in minutes" },
        new() { Key = "JWT_REFRESH_EXPIRY_DAYS", Value = "30", Description = "Refresh token lifetime in days" },

        // DHL
        new() { Key = "DHL_API_KEY", Value = "PLACEHOLDER_DHL_API_KEY", Description = "DHL MyDHL API username / site ID", IsSecret = true },
        new() { Key = "DHL_API_SECRET", Value = "PLACEHOLDER_DHL_API_SECRET", Description = "DHL MyDHL API password", IsSecret = true },
        new() { Key = "DHL_ACCOUNT_NUMBER", Value = "PLACEHOLDER_DHL_ACCOUNT", Description = "DHL export (Canada-origin) shipper account number", IsSecret = true },
        new() { Key = "DHL_IMPORT_ACCOUNT_NUMBER", Value = "PLACEHOLDER_DHL_IMPORT_ACCOUNT", Description = "DHL import / IMPEX-enabled account number", IsSecret = true },
        new() { Key = "DHL_IMPORT_ACCOUNT", Value = "PLACEHOLDER_DHL_IMPORT_ACCOUNT", Description = "DHL import/payer account for non-Canada-origin shipments", IsSecret = true },
        new() { Key = "DHL_BASE_URL", Value = "https://express.api.dhl.com/mydhlapi/test", Description = "DHL API base URL (test or production)" },
        new() { Key = "DHL_TIMEOUT_SECONDS", Value = "30", Description = "HTTP timeout in seconds for DHL API calls" },

        // Stripe — set real keys via direct DB update or admin API before going live
        new() { Key = "STRIPE_SECRET_KEY",       Value = "PLACEHOLDER_STRIPE_SECRET_KEY",       Description = "Stripe secret key (sk_test_... or sk_live_...)", IsSecret = true },
        new() { Key = "STRIPE_WEBHOOK_SECRET",   Value = "PLACEHOLDER_STRIPE_WEBHOOK_SECRET",   Description = "Stripe webhook signing secret (whsec_...)", IsSecret = true },
        new() { Key = "STRIPE_PUBLISHABLE_KEY",  Value = "PLACEHOLDER_STRIPE_PUBLISHABLE_KEY",  Description = "Stripe publishable key — expose to frontend only, never used server-side" },

        // Email / Resend
        new() { Key = "email.provider", Value = "resend", Description = "Email provider. Supported: resend, disabled." },
        new() { Key = "Email:Resend:ApiKey", Value = "PLACEHOLDER_RESEND_API_KEY", Description = "Resend API key used to send transactional email", IsSecret = true },
        new() { Key = "Email:Resend:From", Value = "no-reply@ryverental.info", Description = "Verified sender email address for transactional email" },
        new() { Key = "Email:Resend:FromName", Value = "RyveSwift", Description = "Sender display name for transactional email" },
        new() { Key = "Email:ReplyTo", Value = "support@ryvepool.com", Description = "Reply-to email address for transactional email" },
        new() { Key = "Email:AdminRecipients", Value = "", Description = "Comma-separated admin alert recipients. Falls back to active admin users when blank." },
        new() { Key = "Email:PasswordResetExpiryMinutes", Value = "30", Description = "Password reset link lifetime in minutes." },
        new() { Key = "Email:SubjectPrefix", Value = "RyveSwift", Description = "Prefix applied to outbound email subjects." },
        new() { Key = "App:PublicBaseUrl", Value = "https://swift.ryvepos.com", Description = "Public API base URL used to build email action links." },
        new() { Key = "App:FrontendBaseUrl", Value = "https://swift.ryvepos.com", Description = "Frontend base URL used to build email links." },

        // Quote settings
        new() { Key = "QUOTE_EXPIRY_HOURS", Value = "24", Description = "How long a quote is valid in hours" },
        new() { Key = "DEFAULT_MARKUP_PERCENT", Value = "20", Description = "Markup % added on top of your DHL discounted rate. 20 = customer pays 20% above your cost. Override per route via markup rules." },
        new() { Key = "DEFAULT_PLATFORM_FEE", Value = "5.00", Description = "Flat platform fee added to every shipment (in billing currency). Applied after the % markup." },
        new() { Key = "DEFAULT_CURRENCY", Value = "CAD", Description = "Default billing currency" },

        // Tracking
        new() { Key = "TRACKING_POLL_INTERVAL_MINUTES", Value = "30", Description = "Interval in minutes for background tracking sync" },
    };

    public static async Task SeedAsync(AppDbContext context)
    {
        bool changed = false;

        foreach (var def in DefaultConfigs)
        {
            var existing = await context.AppConfigs.FirstOrDefaultAsync(c => c.Key == def.Key);
            if (existing is null)
            {
                context.AppConfigs.Add(new AppConfig
                {
                    Key = def.Key,
                    Value = def.Value,
                    Description = def.Description,
                    IsSecret = def.IsSecret,
                    UpdatedAt = DateTime.UtcNow
                });
                changed = true;
            }
            else if (IsPlaceholder(existing.Value) && !IsPlaceholder(def.Value))
            {
                // Upgrade from placeholder to real value
                existing.Value = def.Value;
                existing.UpdatedAt = DateTime.UtcNow;
                changed = true;
            }
        }

        if (changed)
            await context.SaveChangesAsync();

        // Seed default markup rules once
        if (!await context.MarkupRules.AnyAsync())
        {
            context.MarkupRules.AddRange(
                new MarkupRule { OriginCountry = "CA", DestinationCountry = "GH", ProductCode = "P", MarkupPercent = 20, PlatformFee = 5, IsActive = true },
                new MarkupRule { OriginCountry = "CA", DestinationCountry = "NG", ProductCode = "P", MarkupPercent = 20, PlatformFee = 5, IsActive = true },
                new MarkupRule { OriginCountry = "GH", DestinationCountry = "CA", ProductCode = "P", MarkupPercent = 20, PlatformFee = 5, IsActive = true },
                new MarkupRule { OriginCountry = "NG", DestinationCountry = "CA", ProductCode = "P", MarkupPercent = 20, PlatformFee = 5, IsActive = true },
                new MarkupRule { OriginCountry = "US", DestinationCountry = "GH", ProductCode = "P", MarkupPercent = 20, PlatformFee = 5, IsActive = true },
                new MarkupRule { OriginCountry = "US", DestinationCountry = "NG", ProductCode = "P", MarkupPercent = 20, PlatformFee = 5, IsActive = true }
            );
            await context.SaveChangesAsync();
        }
    }

    private static bool IsPlaceholder(string value) =>
        value.StartsWith("PLACEHOLDER_", StringComparison.Ordinal) ||
        value == "CHANGE_THIS_TO_A_STRONG_SECRET_AT_LEAST_32_CHARS";
}
