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
            ALTER TABLE "Shipments" ADD COLUMN IF NOT EXISTS "Incoterm" text NOT NULL DEFAULT 'DAP';
            ALTER TABLE "Shipments" ADD COLUMN IF NOT EXISTS "Currency" text NOT NULL DEFAULT 'CAD';
            ALTER TABLE "Shipments" ADD COLUMN IF NOT EXISTS "TotalAmount" numeric NOT NULL DEFAULT 0;

            ALTER TABLE "Quotes" ADD COLUMN IF NOT EXISTS "DhlBaseRate" numeric NOT NULL DEFAULT 0;
            ALTER TABLE "Quotes" ADD COLUMN IF NOT EXISTS "DhlCurrency" text NOT NULL DEFAULT 'CAD';
            ALTER TABLE "Quotes" ADD COLUMN IF NOT EXISTS "Incoterm" text NOT NULL DEFAULT 'DAP';
            ALTER TABLE "Quotes" ADD COLUMN IF NOT EXISTS "MarkupPercent" numeric NOT NULL DEFAULT 0;
            ALTER TABLE "Quotes" ADD COLUMN IF NOT EXISTS "PlatformFee" numeric NOT NULL DEFAULT 0;
            ALTER TABLE "Quotes" ADD COLUMN IF NOT EXISTS "TotalAmount" numeric NOT NULL DEFAULT 0;
            ALTER TABLE "Quotes" ADD COLUMN IF NOT EXISTS "Currency" text NOT NULL DEFAULT 'CAD';
            ALTER TABLE "Quotes" ADD COLUMN IF NOT EXISTS "RyvePoolDeliverySelected" boolean NOT NULL DEFAULT false;
            ALTER TABLE "Quotes" ADD COLUMN IF NOT EXISTS "RyvePoolDeliveryStatus" text;
            ALTER TABLE "Quotes" ADD COLUMN IF NOT EXISTS "RyvePoolDeliveryDispatchTiming" text;
            ALTER TABLE "Quotes" ADD COLUMN IF NOT EXISTS "RyvePoolDeliveryScheduledForUtc" timestamp with time zone;
            ALTER TABLE "Quotes" ADD COLUMN IF NOT EXISTS "RyvePoolDeliveryFeeMinor" bigint NOT NULL DEFAULT 0;
            ALTER TABLE "Quotes" ADD COLUMN IF NOT EXISTS "RyvePoolDeliveryCurrency" text;
            ALTER TABLE "Quotes" ADD COLUMN IF NOT EXISTS "RyvePoolDeliveryQuoteRawResponse" jsonb;
            ALTER TABLE "Quotes" ADD COLUMN IF NOT EXISTS "RyvePoolPickupName" text;
            ALTER TABLE "Quotes" ADD COLUMN IF NOT EXISTS "RyvePoolPickupPhone" text;
            ALTER TABLE "Quotes" ADD COLUMN IF NOT EXISTS "RyvePoolPickupAddress" text;
            ALTER TABLE "Quotes" ADD COLUMN IF NOT EXISTS "RyvePoolPickupLandmark" text;
            ALTER TABLE "Quotes" ADD COLUMN IF NOT EXISTS "RyvePoolPickupLat" numeric;
            ALTER TABLE "Quotes" ADD COLUMN IF NOT EXISTS "RyvePoolPickupLng" numeric;
            ALTER TABLE "Quotes" ADD COLUMN IF NOT EXISTS "RyvePoolDropoffName" text;
            ALTER TABLE "Quotes" ADD COLUMN IF NOT EXISTS "RyvePoolDropoffPhone" text;
            ALTER TABLE "Quotes" ADD COLUMN IF NOT EXISTS "RyvePoolDropoffEmail" text;
            ALTER TABLE "Quotes" ADD COLUMN IF NOT EXISTS "RyvePoolDropoffAddress" text;
            ALTER TABLE "Quotes" ADD COLUMN IF NOT EXISTS "RyvePoolDropoffLandmark" text;
            ALTER TABLE "Quotes" ADD COLUMN IF NOT EXISTS "RyvePoolDropoffLat" numeric;
            ALTER TABLE "Quotes" ADD COLUMN IF NOT EXISTS "RyvePoolDropoffLng" numeric;
            ALTER TABLE "Quotes" ADD COLUMN IF NOT EXISTS "RyvePoolDhlPointId" text;
            ALTER TABLE "Quotes" ADD COLUMN IF NOT EXISTS "RyvePoolDhlPointName" text;
            ALTER TABLE "Quotes" ADD COLUMN IF NOT EXISTS "RyvePoolRegionCode" text;
            ALTER TABLE "Quotes" ADD COLUMN IF NOT EXISTS "RyvePoolExternalBranchId" text;
            ALTER TABLE "Quotes" ADD COLUMN IF NOT EXISTS "RyvePoolDispatchMode" text;
            ALTER TABLE "Quotes" ADD COLUMN IF NOT EXISTS "RyvePoolPackageType" text;
            ALTER TABLE "Quotes" ADD COLUMN IF NOT EXISTS "RyvePoolParcelWeightKg" numeric;
            ALTER TABLE "Quotes" ADD COLUMN IF NOT EXISTS "RyvePoolDriverInstructions" text;
            ALTER TABLE "Quotes" ADD COLUMN IF NOT EXISTS "RyvePoolVehicleCategoryId" text;

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

            CREATE TABLE IF NOT EXISTS "RyvePoolDeliveries" (
                "Id" uuid NOT NULL,
                "UserId" uuid,
                "QuoteId" uuid,
                "ShipmentId" uuid,
                "Environment" text NOT NULL,
                "ExternalOrderId" text NOT NULL,
                "MerchantReference" text,
                "ExternalBranchId" text,
                "RyvePoolDispatchId" text,
                "Status" text NOT NULL,
                "TrackingUrl" text,
                "RegionCode" text NOT NULL,
                "Timezone" text,
                "DispatchModeRequested" text,
                "DispatchModeUsed" text,
                "DriverPool" text,
                "PaymentType" text NOT NULL,
                "CodAmountMinor" bigint NOT NULL DEFAULT 0,
                "PackageType" text NOT NULL,
                "ParcelWeightKg" numeric,
                "DriverInstructions" text,
                "Currency" text NOT NULL,
                "DeliveryFeeMinor" bigint NOT NULL DEFAULT 0,
                "PlatformFeeMinor" bigint NOT NULL DEFAULT 0,
                "RyvePoolCommissionMinor" bigint NOT NULL DEFAULT 0,
                "DriverPayoutMinor" bigint NOT NULL DEFAULT 0,
                "NotificationFeeMinor" bigint NOT NULL DEFAULT 0,
                "TaxMinor" bigint NOT NULL DEFAULT 0,
                "PaymentProcessingFeeMinor" bigint NOT NULL DEFAULT 0,
                "RefundAdjustmentMinor" bigint NOT NULL DEFAULT 0,
                "SettlementStatus" text,
                "CancellationWindowMinutes" integer,
                "CancellableUntil" timestamp with time zone,
                "CanCancel" boolean,
                "ShortCode" text,
                "DispatchTiming" text,
                "ScheduledForUtc" timestamp with time zone,
                "DispatchAttemptCount" integer NOT NULL DEFAULT 0,
                "LastDispatchAttemptAt" timestamp with time zone,
                "LastDispatchError" text,
                "DhlPointId" text,
                "DhlPointName" text,
                "PickupName" text NOT NULL,
                "PickupPhone" text NOT NULL,
                "PickupAddress" text,
                "PickupLandmark" text,
                "PickupLat" numeric,
                "PickupLng" numeric,
                "DropoffName" text NOT NULL,
                "DropoffPhone" text NOT NULL,
                "DropoffEmail" text,
                "DropoffAddress" text,
                "DropoffLandmark" text,
                "DropoffLat" numeric,
                "DropoffLng" numeric,
                "MetadataJson" text,
                "RawQuoteResponse" text,
                "RawCreateResponse" text,
                "RawLatestResponse" text,
                "CreatedAt" timestamp with time zone NOT NULL,
                "UpdatedAt" timestamp with time zone NOT NULL,
                "PickedUpAt" timestamp with time zone,
                "DeliveredAt" timestamp with time zone,
                "CancelledAt" timestamp with time zone,
                "FailedAt" timestamp with time zone,
                CONSTRAINT "PK_RyvePoolDeliveries" PRIMARY KEY ("Id"),
                CONSTRAINT "FK_RyvePoolDeliveries_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE SET NULL,
                CONSTRAINT "FK_RyvePoolDeliveries_Quotes_QuoteId" FOREIGN KEY ("QuoteId") REFERENCES "Quotes" ("Id") ON DELETE SET NULL,
                CONSTRAINT "FK_RyvePoolDeliveries_Shipments_ShipmentId" FOREIGN KEY ("ShipmentId") REFERENCES "Shipments" ("Id") ON DELETE SET NULL
            );

            ALTER TABLE "RyvePoolDeliveries" ADD COLUMN IF NOT EXISTS "QuoteId" uuid;
            ALTER TABLE "RyvePoolDeliveries" ADD COLUMN IF NOT EXISTS "ShipmentId" uuid;
            ALTER TABLE "RyvePoolDeliveries" ADD COLUMN IF NOT EXISTS "DispatchTiming" text;
            ALTER TABLE "RyvePoolDeliveries" ADD COLUMN IF NOT EXISTS "ScheduledForUtc" timestamp with time zone;
            ALTER TABLE "RyvePoolDeliveries" ADD COLUMN IF NOT EXISTS "DispatchAttemptCount" integer NOT NULL DEFAULT 0;
            ALTER TABLE "RyvePoolDeliveries" ADD COLUMN IF NOT EXISTS "LastDispatchAttemptAt" timestamp with time zone;
            ALTER TABLE "RyvePoolDeliveries" ADD COLUMN IF NOT EXISTS "LastDispatchError" text;
            ALTER TABLE "RyvePoolDeliveries" ADD COLUMN IF NOT EXISTS "DhlPointId" text;
            ALTER TABLE "RyvePoolDeliveries" ADD COLUMN IF NOT EXISTS "DhlPointName" text;
            ALTER TABLE "RyvePoolDeliveries" ADD COLUMN IF NOT EXISTS "RawQuoteResponse" text;

            DO $$
            BEGIN
                IF NOT EXISTS (
                    SELECT 1 FROM pg_constraint WHERE conname = 'FK_RyvePoolDeliveries_Quotes_QuoteId'
                ) THEN
                    ALTER TABLE "RyvePoolDeliveries"
                    ADD CONSTRAINT "FK_RyvePoolDeliveries_Quotes_QuoteId"
                    FOREIGN KEY ("QuoteId") REFERENCES "Quotes" ("Id") ON DELETE SET NULL;
                END IF;

                IF NOT EXISTS (
                    SELECT 1 FROM pg_constraint WHERE conname = 'FK_RyvePoolDeliveries_Shipments_ShipmentId'
                ) THEN
                    ALTER TABLE "RyvePoolDeliveries"
                    ADD CONSTRAINT "FK_RyvePoolDeliveries_Shipments_ShipmentId"
                    FOREIGN KEY ("ShipmentId") REFERENCES "Shipments" ("Id") ON DELETE SET NULL;
                END IF;
            END $$;

            CREATE UNIQUE INDEX IF NOT EXISTS "IX_RyvePoolDeliveries_Environment_ExternalOrderId" ON "RyvePoolDeliveries" ("Environment", "ExternalOrderId");
            CREATE INDEX IF NOT EXISTS "IX_RyvePoolDeliveries_RyvePoolDispatchId" ON "RyvePoolDeliveries" ("RyvePoolDispatchId");
            CREATE INDEX IF NOT EXISTS "IX_RyvePoolDeliveries_QuoteId" ON "RyvePoolDeliveries" ("QuoteId");
            CREATE INDEX IF NOT EXISTS "IX_RyvePoolDeliveries_ShipmentId" ON "RyvePoolDeliveries" ("ShipmentId");

            CREATE TABLE IF NOT EXISTS "RyvePoolWebhookEvents" (
                "Id" uuid NOT NULL,
                "DeliveryId" uuid,
                "RyvePoolEventId" text,
                "Event" text NOT NULL,
                "Environment" text NOT NULL,
                "DispatchId" text,
                "ExternalOrderId" text,
                "PreviousStatus" text,
                "Status" text,
                "SignatureHeader" text,
                "DeliveryHeader" text,
                "IsSignatureValid" boolean NOT NULL,
                "RawPayload" text NOT NULL,
                "ReceivedAt" timestamp with time zone NOT NULL,
                CONSTRAINT "PK_RyvePoolWebhookEvents" PRIMARY KEY ("Id"),
                CONSTRAINT "FK_RyvePoolWebhookEvents_RyvePoolDeliveries_DeliveryId" FOREIGN KEY ("DeliveryId") REFERENCES "RyvePoolDeliveries" ("Id") ON DELETE SET NULL
            );

            CREATE UNIQUE INDEX IF NOT EXISTS "IX_RyvePoolWebhookEvents_RyvePoolEventId" ON "RyvePoolWebhookEvents" ("RyvePoolEventId") WHERE "RyvePoolEventId" IS NOT NULL;
            CREATE INDEX IF NOT EXISTS "IX_RyvePoolWebhookEvents_Environment_DispatchId" ON "RyvePoolWebhookEvents" ("Environment", "DispatchId");
            """);
    }
}
