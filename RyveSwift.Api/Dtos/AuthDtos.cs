namespace RyveSwift.Api.Dtos;

public record RegisterRequest(string Email, string Password, string FullName, string? Phone);
public record LoginRequest(string Email, string Password);
public record LoginResponse(string AccessToken, string RefreshToken, string TokenType, int ExpiresIn, bool PasswordResetRequired);
public record RefreshTokenRequest(string RefreshToken);
public record ForgotPasswordRequest(string Email);
public record ResetPasswordRequest(string Token, string NewPassword);
public record MessageResponse(string Message);
