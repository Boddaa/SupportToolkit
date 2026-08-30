using Microsoft.EntityFrameworkCore;
using NetworkDiscoveryTool.Core.Models;
using NetworkDiscoveryTool.Data;

namespace NetworkDiscoveryTool.UI.Services;

public interface ISettingsService
{
    Task<string?> GetAsync(string key);
    Task<T> GetAsync<T>(string key, T defaultValue) where T : IConvertible;
    Task SetAsync(string key, string value);
    Task SetAsync<T>(string key, T value) where T : IConvertible;
    Task SaveAllAsync(Dictionary<string, string> settings);
    Task<Dictionary<string, string>> GetAllAsync();
}

public sealed class SettingsService : ISettingsService
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;

    public SettingsService(IDbContextFactory<AppDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<string?> GetAsync(string key)
    {
        await using var ctx = await _contextFactory.CreateDbContextAsync();
        var entry = await ctx.AppSettings.FindAsync(key);
        return entry?.Value;
    }

    public async Task<T> GetAsync<T>(string key, T defaultValue) where T : IConvertible
    {
        var val = await GetAsync(key);
        if (val is null) return defaultValue;
        try { return (T)Convert.ChangeType(val, typeof(T)); }
        catch { return defaultValue; }
    }

    public async Task SetAsync(string key, string value)
    {
        await using var ctx = await _contextFactory.CreateDbContextAsync();
        var existing = await ctx.AppSettings.FindAsync(key);
        if (existing is not null)
        {
            existing.Value = value;
        }
        else
        {
            ctx.AppSettings.Add(new AppSetting { Key = key, Value = value });
        }
        await ctx.SaveChangesAsync();
    }

    public async Task SetAsync<T>(string key, T value) where T : IConvertible
    {
        await SetAsync(key, value?.ToString() ?? "");
    }

    public async Task SaveAllAsync(Dictionary<string, string> settings)
    {
        await using var ctx = await _contextFactory.CreateDbContextAsync();
        foreach (var (key, value) in settings)
        {
            var existing = await ctx.AppSettings.FindAsync(key);
            if (existing is not null)
            {
                existing.Value = value;
            }
            else
            {
                ctx.AppSettings.Add(new AppSetting { Key = key, Value = value });
            }
        }
        await ctx.SaveChangesAsync();
    }

    public async Task<Dictionary<string, string>> GetAllAsync()
    {
        await using var ctx = await _contextFactory.CreateDbContextAsync();
        return await ctx.AppSettings
            .AsNoTracking()
            .ToDictionaryAsync(e => e.Key, e => e.Value);
    }
}
