namespace RyveSwift.Api.Dtos;

public record AdminShipmentResponse(
    Guid Id, Guid? UserId, string? UserEmail,
    string? TrackingNumber, string Status,
    string OriginCountry, string DestinationCountry,
    string ProductCode,
    decimal TotalAmount,
    decimal? DhlBaseRate,
    decimal? MarkupPercent,
    decimal? PlatformFee,
    decimal MarkupRevenue,
    decimal GrossProfit,
    string Currency, DateTime CreatedAt);

public record AdminUserResponse(
    Guid Id, string Email, string? FullName, string? Phone,
    string Role,
    bool IsSuspended,
    DateTime? SuspendedAt,
    string? SuspendedReason,
    DateTime? DeletedAt,
    bool PasswordResetRequired,
    DateTime? EmailUnsubscribedAt,
    DateTime CreatedAt,
    DateTime? LastLogin);

public record AdminUserDetailResponse(
    Guid Id,
    string Email,
    string? FullName,
    string? Phone,
    string Role,
    bool IsSuspended,
    DateTime? SuspendedAt,
    string? SuspendedReason,
    DateTime? DeletedAt,
    bool PasswordResetRequired,
    DateTime? PasswordChangedAt,
    DateTime? EmailUnsubscribedAt,
    DateTime CreatedAt,
    DateTime? LastLogin,
    int ShipmentCount,
    decimal LifetimeRevenue,
    DateTime? LastShipmentAt);

public record AdminCreateUserRequest(
    string Email,
    string? Password,
    string? FullName,
    string? Phone,
    string? Role);

public record AdminUpdateUserRequest(
    string? Email,
    string? FullName,
    string? Phone,
    string? Role);

public record AdminCreateUserResponse(
    AdminUserResponse User,
    string? TemporaryPassword);

public record AdminSuspendUserRequest(string? Reason);

public record AdminResetPasswordRequest(
    string? NewPassword,
    bool? RequirePasswordChange);

public record AdminResetPasswordResponse(
    Guid UserId,
    string Email,
    bool PasswordResetRequired,
    string? TemporaryPassword);

public record AdminSendTestEmailRequest(string ToEmail);

public record AdminSendEmailRequest(
    string ToEmail,
    string Subject,
    string? TextBody,
    string? HtmlBody);

public record AdminEmailSendResponse(
    string Status,
    string? MessageId,
    string? Error);

public record AdminEmailConfigResponse(
    string Provider,
    string FromEmail,
    string FromName,
    string ReplyTo,
    string AdminRecipients,
    string SubjectPrefix,
    string PublicBaseUrl,
    bool ResendApiKeyConfigured);

public record AdminUpdateEmailConfigRequest(
    string? Provider,
    string? ResendApiKey,
    string? FromEmail,
    string? FromName,
    string? ReplyTo,
    string? AdminRecipients,
    string? SubjectPrefix,
    string? PublicBaseUrl);

public record RevenueReportResponse(
    DateTime From,
    DateTime To,
    string Currency,
    decimal TotalRevenue,
    decimal DhlBaseCost,
    decimal MarkupEarned,
    int TotalShipments,
    int PaidShipments,
    RevenueSplitResponse RevenueSplit,
    AdminOperationalMetrics Operations,
    AdminFunnelMetrics Funnel,
    AdminCustomerMetrics Customers,
    IReadOnlyList<AdminTimeSeriesPoint> TimeSeries,
    IReadOnlyList<AdminRouteMetric> TopRoutes,
    IReadOnlyList<AdminStatusMetric> StatusBreakdown,
    IReadOnlyList<AdminProductMetric> ProductMix,
    IReadOnlyList<AdminTopCustomerMetric> TopCustomers);

public record RevenueSplitResponse(
    decimal CustomerRevenue,
    decimal DhlActuallyCharged,
    decimal MarkupRevenue,
    decimal PlatformFees,
    decimal GrossProfit,
    decimal GrossMarginPercent,
    decimal AverageOrderValue,
    decimal AverageDhlCharge,
    decimal AverageMarkupRevenue,
    decimal AveragePlatformFee,
    decimal AverageGrossProfit,
    decimal AverageMarkupPercentApplied,
    int ShipmentsMissingDhlCharge);

public record AdminOperationalMetrics(
    int ShipmentsCreated,
    int RevenueShipments,
    int LabelsGenerated,
    int InTransit,
    int Delivered,
    int PendingPayment,
    int Refunded,
    int Cancelled,
    int Exceptions,
    int DhlBookingFailures,
    decimal DhlBookingFailureRatePercent,
    decimal TotalWeightKg,
    decimal AverageWeightKg,
    decimal? AverageMinutesToLabel);

public record AdminFunnelMetrics(
    int QuotesCreated,
    int GuestQuotes,
    int RegisteredQuotes,
    int ExpiredQuotes,
    int PaymentIntentsCreated,
    int SucceededPayments,
    int FailedPayments,
    int PendingPayments,
    int RefundedPayments,
    decimal QuoteToShipmentRatePercent,
    decimal QuoteToPaidShipmentRatePercent,
    decimal PaymentSuccessRatePercent);

public record AdminCustomerMetrics(
    int TotalCustomers,
    int NewCustomers,
    int ActiveCustomers,
    int RepeatCustomers,
    decimal AverageRevenuePerActiveCustomer,
    decimal AverageShipmentsPerActiveCustomer);

public record AdminTimeSeriesPoint(
    DateTime PeriodStart,
    string Period,
    int Quotes,
    int Shipments,
    int PaidShipments,
    int NewCustomers,
    decimal Revenue,
    decimal DhlActuallyCharged,
    decimal MarkupRevenue,
    decimal PlatformFees,
    decimal GrossProfit);

public record AdminRouteMetric(
    string Route,
    string OriginCountry,
    string DestinationCountry,
    int Shipments,
    decimal Revenue,
    decimal DhlActuallyCharged,
    decimal MarkupRevenue,
    decimal PlatformFees,
    decimal GrossProfit,
    decimal GrossMarginPercent,
    decimal AverageRevenue,
    decimal AverageWeightKg);

public record AdminStatusMetric(
    string Status,
    int Count,
    decimal Revenue);

public record AdminProductMetric(
    string ProductCode,
    string Service,
    int Shipments,
    decimal Revenue,
    decimal GrossProfit,
    decimal AverageRevenue);

public record AdminTopCustomerMetric(
    Guid UserId,
    string? Email,
    int Shipments,
    decimal Revenue,
    decimal GrossProfit,
    DateTime LastShipmentAt);

public record MarkupRuleRequest(
    string? OriginCountry,
    string? DestinationCountry,
    decimal? MinWeightKg,
    decimal? MaxWeightKg,
    string? ProductCode,
    decimal MarkupPercent,
    decimal PlatformFee);

public record MarkupRuleResponse(
    Guid Id,
    string? OriginCountry,
    string? DestinationCountry,
    decimal? MinWeightKg,
    decimal? MaxWeightKg,
    string? ProductCode,
    decimal MarkupPercent,
    decimal PlatformFee,
    bool IsActive,
    DateTime CreatedAt);
