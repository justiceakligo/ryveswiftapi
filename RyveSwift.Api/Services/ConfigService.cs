using Microsoft.EntityFrameworkCore;
using RyveSwift.Api.Data;
using RyveSwift.Api.Entities;

namespace RyveSwift.Api.Services;

public class ConfigService
{
    private Dictionary<string, string> _cache;
    private readonly IServiceScopeFactory _scopeFactory;

    // Constructor used for DI (with scope factory for DB reload)
    public ConfigService(Dictionary<string, string> bootstrapValues, IServiceScopeFactory scopeFactory)
    {
        _cache = bootstrapValues;
        _scopeFactory = scopeFactory;
    }

    public string Get(string key)
    {
        if (_cache.TryGetValue(key, out var value))
            return value;
        throw new InvalidOperationException($"Configuration key '{key}' not found in database. Please seed AppConfig table.");
    }

    public string Get(string key, string defaultValue)
    {
        return _cache.TryGetValue(key, out var value) ? value : defaultValue;
    }

    public int GetInt(string key, int defaultValue = 0)
    {
        if (_cache.TryGetValue(key, out var value) && int.TryParse(value, out var result))
            return result;
        return defaultValue;
    }

    public decimal GetDecimal(string key, decimal defaultValue = 0m)
    {
        if (_cache.TryGetValue(key, out var value) && decimal.TryParse(value, out var result))
            return result;
        return defaultValue;
    }

    public async Task ReloadAsync()
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        _cache = await db.AppConfigs.ToDictionaryAsync(c => c.Key, c => c.Value);
    }

    public async Task SetAsync(string key, string value)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var config = await db.AppConfigs.FirstOrDefaultAsync(c => c.Key == key);
        if (config is null)
        {
            db.AppConfigs.Add(new AppConfig { Key = key, Value = value, UpdatedAt = DateTime.UtcNow });
        }
        else
        {
            config.Value = value;
            config.UpdatedAt = DateTime.UtcNow;
        }
        await db.SaveChangesAsync();
        _cache[key] = value;
    }
}
