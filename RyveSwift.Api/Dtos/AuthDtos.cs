namespace RyveSwift.Api.Dtos;

public record RegisterRequest(string Email, string Password, string FullName, string? Phone);
public record LoginRequest(string Email, string Password);
public record LoginResponse(string AccessToken, string RefreshToken, string TokenType, int ExpiresIn);
public record RefreshTokenRequest(string RefreshToken);
