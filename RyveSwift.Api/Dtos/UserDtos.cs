namespace RyveSwift.Api.Dtos;

public record UserProfileResponse(
    Guid Id, string Email, string? Phone, string? FullName, string Role, DateTime CreatedAt);

public record UpdateProfileRequest(string? FullName, string? Phone);
