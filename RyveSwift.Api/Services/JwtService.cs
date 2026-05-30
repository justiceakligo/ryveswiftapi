using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using RyveSwift.Api.Entities;

namespace RyveSwift.Api.Services;

public class JwtService
{
    private readonly ConfigService _config;

    public JwtService(ConfigService config)
    {
        _config = config;
    }

    public string GenerateAccessToken(User user)
    {
        var secret = _config.Get("JWT_SECRET");
        var issuer = _config.Get("JWT_ISSUER");
        var audience = _config.Get("JWT_AUDIENCE");
        var expiryMinutes = _config.GetInt("JWT_EXPIRY_MINUTES", 60);

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim(ClaimTypes.Role, user.Role),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(expiryMinutes),
            signingCredentials: credentials
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public (string token, string tokenHash, DateTime expiresAt) GenerateRefreshToken()
    {
        var expiryDays = _config.GetInt("JWT_REFRESH_EXPIRY_DAYS", 30);
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        var tokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
        return (token, tokenHash, DateTime.UtcNow.AddDays(expiryDays));
    }

    public string HashRefreshToken(string token)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    }
}
