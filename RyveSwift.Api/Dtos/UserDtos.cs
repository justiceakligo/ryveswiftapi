namespace RyveSwift.Api.Dtos;

public record UserProfileResponse(
    Guid Id,
    string Email,
    string? Phone,
    string? FullName,
    string Role,
    bool PasswordResetRequired,
    DateTime? EmailUnsubscribedAt,
    DateTime CreatedAt);

public record UpdateProfileRequest(string? FullName, string? Phone);

public record ChangePasswordRequest(string CurrentPassword, string NewPassword);
