using System.Security.Cryptography;
using System.Text;

namespace RyveSwift.Api.Services;

public class EmailPreferenceTokenService
{
    private readonly ConfigService _config;

    public EmailPreferenceTokenService(ConfigService config)
    {
        _config = config;
    }

    public string CreateToken(Guid userId)
    {
        var payload = userId.ToString("N");
        var signature = Sign(payload);
        return $"{Base64UrlEncode(Encoding.UTF8.GetBytes(payload))}.{Base64UrlEncode(signature)}";
    }

    public bool TryValidate(string? token, out Guid userId)
    {
        userId = default;
        if (string.IsNullOrWhiteSpace(token)) return false;

        var parts = token.Split('.', 2);
        if (parts.Length != 2) return false;

        try
        {
            var payload = Encoding.UTF8.GetString(Base64UrlDecode(parts[0]));
            var providedSignature = Base64UrlDecode(parts[1]);
            var expectedSignature = Sign(payload);

            if (!CryptographicOperations.FixedTimeEquals(providedSignature, expectedSignature))
                return false;

            return Guid.TryParseExact(payload, "N", out userId);
        }
        catch
        {
            return false;
        }
    }

    private byte[] Sign(string payload)
    {
        var secret = _config.Get("JWT_SECRET", "");
        if (string.IsNullOrWhiteSpace(secret))
            secret = Environment.GetEnvironmentVariable("Auth__Jwt__SigningKey") ?? "";

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static byte[] Base64UrlDecode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded = padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '=');
        return Convert.FromBase64String(padded);
    }
}
