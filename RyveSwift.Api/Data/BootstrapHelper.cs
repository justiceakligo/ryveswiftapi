using Microsoft.EntityFrameworkCore;

namespace RyveSwift.Api.Data;

public static class BootstrapHelper
{
    public static async Task<Dictionary<string, string>> InitializeAndLoadConfigAsync(string connectionString)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        await using var context = new AppDbContext(options);

        // Create tables if they don't exist (idempotent for first run)
        await context.Database.EnsureCreatedAsync();

        // Seed default config values
        await DatabaseSeeder.SeedAsync(context);

        // Load all config into dictionary
        var configs = await context.AppConfigs.ToDictionaryAsync(c => c.Key, c => c.Value);
        return configs;
    }
}
