namespace RyveSwift.Api.Common;

/// <summary>Per-field validation error.</summary>
public record FieldError(string Field, string Message);

/// <summary>Inner error detail — serialises as the value of the top-level "error" key.</summary>
public record ApiErrorBody(string Code, string Message, IReadOnlyList<FieldError> Details);

/// <summary>
/// All non-2xx responses use { "error": { "code", "message", "details" } }.
/// </summary>
public class ApiError
{
    public ApiErrorBody Error { get; }

    public ApiError(string code, string message, IReadOnlyList<FieldError>? details = null)
    {
        Error = new ApiErrorBody(code, message, details ?? Array.Empty<FieldError>());
    }
}
