using System.Net;
using System.Text.Json;
using RyveSwift.Api.Common;
using RyveSwift.Api.Services;

namespace RyveSwift.Api.Middleware;

public class ErrorHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ErrorHandlingMiddleware> _logger;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public ErrorHandlingMiddleware(RequestDelegate next, ILogger<ErrorHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (DhlException ex)
        {
            _logger.LogWarning(ex, "DHL error: {ErrorCode}", ex.ErrorCode);
            await WriteError(context, HttpStatusCode.BadGateway, ex.ErrorCode, ex.Message);
        }
        catch (UnauthorizedAccessException)
        {
            await WriteError(context, HttpStatusCode.Unauthorized, "UNAUTHORIZED", "Authentication required.");
        }
        catch (KeyNotFoundException ex)
        {
            await WriteError(context, HttpStatusCode.NotFound, "NOT_FOUND", ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception for {Method} {Path}", context.Request.Method, context.Request.Path);
            await WriteError(context, HttpStatusCode.InternalServerError, "INTERNAL_ERROR",
                "An unexpected error occurred. Please try again later.");
        }
    }

    private static async Task WriteError(HttpContext context, HttpStatusCode statusCode, string errorCode, string message)
    {
        if (context.Response.HasStarted) return;

        context.Response.StatusCode = (int)statusCode;
        context.Response.ContentType = "application/json";

        var error = new ApiError(errorCode, message);
        var json = JsonSerializer.Serialize(error, JsonOpts);
        await context.Response.WriteAsync(json);
    }
}
