using Stripe;

namespace RyveSwift.Api.Services;

public class StripeService
{
    private readonly ConfigService _config;
    private readonly ILogger<StripeService> _logger;

    public StripeService(ConfigService config, ILogger<StripeService> logger)
    {
        _config = config;
        _logger = logger;
    }

    public async Task<PaymentIntent> CreatePaymentIntentAsync(
        decimal amount, string currency, Guid quoteId, string? idempotencyKey = null)
    {
        StripeConfiguration.ApiKey = _config.Get("STRIPE_SECRET_KEY");

        var amountInCents = (long)Math.Round(amount * 100);

        var options = new PaymentIntentCreateOptions
        {
            Amount = amountInCents,
            Currency = currency.ToLower(),
            Metadata = new Dictionary<string, string>
            {
                { "quote_id", quoteId.ToString() },
                { "ryveswift", "booking" }
            },
            AutomaticPaymentMethods = new PaymentIntentAutomaticPaymentMethodsOptions
            {
                Enabled = true
            }
        };

        var requestOptions = idempotencyKey is not null
            ? new RequestOptions { IdempotencyKey = idempotencyKey }
            : null;

        var service = new PaymentIntentService();
        var intent = await service.CreateAsync(options, requestOptions);

        _logger.LogInformation("Created Stripe PaymentIntent {IntentId} for quote {QuoteId}", intent.Id, quoteId);
        return intent;
    }

    public async Task<PaymentIntent> GetPaymentIntentAsync(string paymentIntentId)
    {
        StripeConfiguration.ApiKey = _config.Get("STRIPE_SECRET_KEY");
        return await new PaymentIntentService().GetAsync(paymentIntentId);
    }

    public async Task<Refund> RefundPaymentIntentAsync(string paymentIntentId)
    {
        StripeConfiguration.ApiKey = _config.Get("STRIPE_SECRET_KEY");
        var options = new RefundCreateOptions { PaymentIntent = paymentIntentId };
        return await new RefundService().CreateAsync(options);
    }

    public Event ConstructWebhookEvent(string json, string stripeSignature)
    {
        var webhookSecret = _config.Get("STRIPE_WEBHOOK_SECRET");
        StripeConfiguration.ApiKey = _config.Get("STRIPE_SECRET_KEY");

        try
        {
            return EventUtility.ConstructEvent(json, stripeSignature, webhookSecret);
        }
        catch (StripeException ex)
        {
            _logger.LogWarning("Stripe webhook signature validation failed: {Error}", ex.Message);
            throw;
        }
    }
}
