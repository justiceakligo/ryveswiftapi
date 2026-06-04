using System.Text;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;
using RyveSwift.Api.Data;
using RyveSwift.Api.Endpoints;
using RyveSwift.Api.Middleware;
using RyveSwift.Api.Services;

// Required for Npgsql timestamp handling with .NET DateTime
AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Postgres")
    ?? throw new InvalidOperationException("Connection string 'Postgres' not found in configuration.");

// ─── Phase 0: Bootstrap — initialize DB and load all config from DB ────────
var dbConfig = await BootstrapHelper.InitializeAndLoadConfigAsync(connectionString);

// ─── Phase 1: Configure services ───────────────────────────────────────────

// PostgreSQL + EF Core
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString));

// JWT Authentication — secrets come from DB config
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = dbConfig.TryGetValue("JWT_ISSUER", out var issuer) ? issuer : "afriswift-api",
            ValidateAudience = true,
            ValidAudience = dbConfig.TryGetValue("JWT_AUDIENCE", out var audience) ? audience : "afriswift-clients",
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(dbConfig.TryGetValue("JWT_SECRET", out var secret)
                    ? secret
                    : throw new InvalidOperationException("JWT_SECRET not found in AppConfig table."))),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30)
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", policy => policy.RequireRole("Admin"));
});

// Rate limiting
builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("auth", o =>
    {
        o.PermitLimit = 10;
        o.Window = TimeSpan.FromMinutes(1);
    });
    options.AddFixedWindowLimiter("quotes", o =>
    {
        o.PermitLimit = 20;
        o.Window = TimeSpan.FromMinutes(1);
    });
    options.AddFixedWindowLimiter("general", o =>
    {
        o.PermitLimit = 100;
        o.Window = TimeSpan.FromMinutes(1);
    });
    options.RejectionStatusCode = 429;
});

// HTTP clients
builder.Services.AddHttpClient("dhl");
builder.Services.AddHttpClient("resend", client =>
{
    client.BaseAddress = new Uri("https://api.resend.com/");
    client.Timeout = TimeSpan.FromSeconds(20);
});

// Application services
builder.Services.AddSingleton(sp =>
    new ConfigService(dbConfig, sp.GetRequiredService<IServiceScopeFactory>()));
builder.Services.AddScoped<JwtService>();
builder.Services.AddScoped<DhlService>();
builder.Services.AddScoped<LocationSuggestionService>();
builder.Services.AddScoped<StripeService>();
builder.Services.AddScoped<MarkupService>();
builder.Services.AddScoped<SpacesStorageService>();
builder.Services.AddScoped<IEmailService, EmailDispatcher>();
builder.Services.AddScoped<EmailPreferenceTokenService>();
builder.Services.AddScoped<NotificationEmailService>();
builder.Services.AddHostedService<TrackingPollingService>();

// OpenAPI / Scalar
builder.Services.AddOpenApi();

// CORS
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod());
});

// ─── Build app ─────────────────────────────────────────────────────────────
var app = builder.Build();

// ─── Middleware pipeline ───────────────────────────────────────────────────
app.UseMiddleware<ErrorHandlingMiddleware>();
app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.Use(async (context, next) =>
{
    if (context.User.Identity?.IsAuthenticated == true)
    {
        var sub = context.User.FindFirstValue(ClaimTypes.NameIdentifier)
               ?? context.User.FindFirstValue("sub");

        if (Guid.TryParse(sub, out var userId))
        {
            var db = context.RequestServices.GetRequiredService<AppDbContext>();
            var userStatus = await db.Users
                .AsNoTracking()
                .Where(u => u.Id == userId)
                .Select(u => new { u.IsSuspended, u.DeletedAt })
                .FirstOrDefaultAsync();

            if (userStatus is null || userStatus.DeletedAt.HasValue)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            if (userStatus.IsSuspended)
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }
        }
    }

    await next();
});
app.UseAuthorization();

// OpenAPI docs (always available, secure in prod via network policy)
app.MapOpenApi();
app.MapScalarApiReference(options =>
{
    options.Title = "AfriSwift Logistics API";
    options.Theme = ScalarTheme.Default;
});

// ─── Health ────────────────────────────────────────────────────────────────
app.MapGet("/health", () => Results.Ok(new { status = "ok", timestamp = DateTime.UtcNow }))
    .WithTags("Health")
    .AllowAnonymous();

// ─── Endpoint registration ─────────────────────────────────────────────────
app.MapAuthEndpoints();
app.MapUserEndpoints();
app.MapAddressEndpoints();
app.MapLocationEndpoints();
app.MapQuoteEndpoints();
app.MapShipmentEndpoints();
app.MapPaymentEndpoints();
app.MapEmailPreferenceEndpoints();
app.MapTrackingEndpoints();
app.MapBookingEndpoints();
app.MapAdminEndpoints();

app.Run();
