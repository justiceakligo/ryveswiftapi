using Microsoft.EntityFrameworkCore;

namespace RyveSwift.Api.Data;

public static class BootstrapHelper
{
    public static async Task<Dictionary<string, string>> InitializeAndLoadConfigAsync(string connectionString)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        await using var context = new AppDbContext(options);

        // Create tables if they don't exist (idempotent for first run)
        await context.Database.EnsureCreatedAsync();

        await ApplySchemaUpgradesAsync(context);

        // Seed default config values
        await DatabaseSeeder.SeedAsync(context);

        // Load all config into dictionary
        var configs = await context.AppConfigs.ToDictionaryAsync(c => c.Key, c => c.Value);
        return configs;
    }

    private static async Task ApplySchemaUpgradesAsync(AppDbContext context)
    {
        // EnsureCreated creates the initial schema but does not migrate existing databases.
        // Keep additive production-safe fixes here until the project moves to EF migrations.
        await context.Database.ExecuteSqlRawAsync("""
            ALTER TABLE "Shipments" ADD COLUMN IF NOT EXISTS "DhlBaseRate" numeric;
            ALTER TABLE "Shipments" ADD COLUMN IF NOT EXISTS "MarkupPercent" numeric;
            ALTER TABLE "Shipments" ADD COLUMN IF NOT EXISTS "PlatformFee" numeric;
            ALTER TABLE "Shipments" ADD COLUMN IF NOT EXISTS "ProductCode" text NOT NULL DEFAULT 'P';
            ALTER TABLE "Shipments" ADD COLUMN IF NOT EXISTS "Currency" text NOT NULL DEFAULT 'CAD';
            ALTER TABLE "Shipments" ADD COLUMN IF NOT EXISTS "TotalAmount" numeric NOT NULL DEFAULT 0;

            ALTER TABLE "Quotes" ADD COLUMN IF NOT EXISTS "DhlBaseRate" numeric NOT NULL DEFAULT 0;
            ALTER TABLE "Quotes" ADD COLUMN IF NOT EXISTS "DhlCurrency" text NOT NULL DEFAULT 'CAD';
            ALTER TABLE "Quotes" ADD COLUMN IF NOT EXISTS "MarkupPercent" numeric NOT NULL DEFAULT 0;
            ALTER TABLE "Quotes" ADD COLUMN IF NOT EXISTS "PlatformFee" numeric NOT NULL DEFAULT 0;
            ALTER TABLE "Quotes" ADD COLUMN IF NOT EXISTS "TotalAmount" numeric NOT NULL DEFAULT 0;
            ALTER TABLE "Quotes" ADD COLUMN IF NOT EXISTS "Currency" text NOT NULL DEFAULT 'CAD';

            ALTER TABLE "Users" ADD COLUMN IF NOT EXISTS "IsSuspended" boolean NOT NULL DEFAULT false;
            ALTER TABLE "Users" ADD COLUMN IF NOT EXISTS "SuspendedAt" timestamp with time zone;
            ALTER TABLE "Users" ADD COLUMN IF NOT EXISTS "SuspendedReason" text;
            ALTER TABLE "Users" ADD COLUMN IF NOT EXISTS "DeletedAt" timestamp with time zone;
            ALTER TABLE "Users" ADD COLUMN IF NOT EXISTS "PasswordResetRequired" boolean NOT NULL DEFAULT false;
            ALTER TABLE "Users" ADD COLUMN IF NOT EXISTS "PasswordChangedAt" timestamp with time zone;
            ALTER TABLE "Users" ADD COLUMN IF NOT EXISTS "EmailUnsubscribedAt" timestamp with time zone;

            CREATE TABLE IF NOT EXISTS "PasswordResetTokens" (
                "Id" uuid NOT NULL,
                "UserId" uuid NOT NULL,
                "TokenHash" text NOT NULL,
                "ExpiresAt" timestamp with time zone NOT NULL,
                "UsedAt" timestamp with time zone,
                "CreatedAt" timestamp with time zone NOT NULL,
                "CreatedIp" text,
                CONSTRAINT "PK_PasswordResetTokens" PRIMARY KEY ("Id"),
                CONSTRAINT "FK_PasswordResetTokens_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE CASCADE
            );

            CREATE UNIQUE INDEX IF NOT EXISTS "IX_PasswordResetTokens_TokenHash" ON "PasswordResetTokens" ("TokenHash");
            CREATE INDEX IF NOT EXISTS "IX_PasswordResetTokens_UserId_UsedAt_ExpiresAt" ON "PasswordResetTokens" ("UserId", "UsedAt", "ExpiresAt");
            """);
    }
}
