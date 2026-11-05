using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NetworkDiscoveryTool.Server.Data;
using NetworkDiscoveryTool.Server.Models;

namespace NetworkDiscoveryTool.Server.Services;

public sealed class TelegramPollingService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly IConfiguration _config;
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(35) };
    private long _offset = 0;

    public TelegramPollingService(IServiceProvider services, IConfiguration config)
    {
        _services = services;
        _config = config;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var token = _config["Telegram:BotToken"] ?? "8914418594:AAGMMqY91qu0MnkEM453gjclAm_RyOyGxfc";
        if (string.IsNullOrWhiteSpace(token))
        {
            Console.WriteLine("[TelegramBot] No Bot Token configured. Polling disabled.");
            return;
        }

        // Delete any existing webhook so Telegram routes all updates to getUpdates polling immediately
        try
        {
            await HttpClient.GetAsync($"https://api.telegram.org/bot{token}/deleteWebhook?drop_pending_updates=false", stoppingToken);
            Console.WriteLine("[TelegramBot] Polling service active. Webhook cleared.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[TelegramBot] Failed to clear webhook: {ex.Message}");
        }

        // Wait 2 seconds for server startup to complete
        await Task.Delay(2000, stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var url = $"https://api.telegram.org/bot{token}/getUpdates?offset={_offset}&timeout=20";
                var response = await HttpClient.GetAsync(url, stoppingToken);

                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync(stoppingToken);
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;

                    if (root.TryGetProperty("result", out var results) && results.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var update in results.EnumerateArray())
                        {
                            if (update.TryGetProperty("update_id", out var uid))
                            {
                                _offset = uid.GetInt64() + 1;
                            }

                            if (update.TryGetProperty("callback_query", out var cb))
                            {
                                await HandleCallbackQueryAsync(cb);
                            }
                        }
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Telegram Polling Error] {ex.Message}");
                await Task.Delay(3000, stoppingToken);
            }
        }
    }

    private async Task HandleCallbackQueryAsync(JsonElement cb)
    {
        try
        {
            var data = cb.TryGetProperty("data", out var d) ? d.GetString() ?? "" : "";
            var queryId = cb.TryGetProperty("id", out var qid) ? qid.GetString() ?? "" : "";

            using var scope = _services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ServerDbContext>();
            var telegram = scope.ServiceProvider.GetRequiredService<TelegramNotifier>();

            // 1. sub:{months}:{userId} -> Extend/Activate subscription
            if (data.StartsWith("sub:", StringComparison.OrdinalIgnoreCase))
            {
                var parts = data.Split(':');
                if (parts.Length >= 3 && int.TryParse(parts[1], out int months) && int.TryParse(parts[2], out int userId))
                {
                    var user = await db.Users.FindAsync(userId);
                    if (user != null)
                    {
                        var baseDate = (user.SubscriptionExpiresAt.HasValue && user.SubscriptionExpiresAt.Value > DateTime.UtcNow)
                            ? user.SubscriptionExpiresAt.Value
                            : DateTime.UtcNow;

                        user.SubscriptionExpiresAt = baseDate.AddMonths(months);
                        user.Status = nameof(UserSubscriptionStatus.Subscribed);

                        // Unblock all devices bound to this user
                        var boundHwids = await db.UserDevices.Where(ud => ud.UserId == user.Id).Select(ud => ud.HardwareId).ToListAsync();
                        foreach (var hw in boundHwids)
                        {
                            var dev = await db.Devices.FirstOrDefaultAsync(d => d.HardwareId == hw);
                            if (dev != null)
                            {
                                dev.IsBlocked = false;
                                dev.BlockReason = null;
                            }
                        }

                        // Also unblock the original registered device if any
                        if (!string.IsNullOrWhiteSpace(user.LastLoginHwid))
                        {
                            var origDev = await db.Devices.FirstOrDefaultAsync(d => d.HardwareId == user.LastLoginHwid);
                            if (origDev != null)
                            {
                                origDev.IsBlocked = false;
                                origDev.BlockReason = null;
                            }
                        }

                        await db.SaveChangesAsync();

                        await telegram.AnswerCallbackQueryAsync(queryId, $"✅ {user.Username} activated for {months} Month(s)!");
                        await telegram.SendSubscriptionExtendedAlertAsync(user.Username, months, user.SubscriptionExpiresAt.Value);
                        return;
                    }
                }
            }

            // 2. ext:{days}:{userId} -> Extend 24h trial
            if (data.StartsWith("ext:", StringComparison.OrdinalIgnoreCase))
            {
                var parts = data.Split(':');
                if (parts.Length >= 3 && int.TryParse(parts[1], out int days) && int.TryParse(parts[2], out int userId))
                {
                    var user = await db.Users.FindAsync(userId);
                    if (user != null)
                    {
                        var baseDate = (user.TrialExpiresAt.HasValue && user.TrialExpiresAt.Value > DateTime.UtcNow)
                            ? user.TrialExpiresAt.Value
                            : DateTime.UtcNow;

                        user.TrialExpiresAt = baseDate.AddDays(days);
                        user.Status = nameof(UserSubscriptionStatus.Trial);

                        if (!string.IsNullOrWhiteSpace(user.LastLoginHwid))
                        {
                            var dev = await db.Devices.FirstOrDefaultAsync(d => d.HardwareId == user.LastLoginHwid);
                            if (dev != null)
                            {
                                dev.TrialExpiresAt = user.TrialExpiresAt.Value;
                                dev.IsTrialConsumed = false;
                                dev.IsBlocked = false;
                            }
                        }

                        await db.SaveChangesAsync();
                        await telegram.AnswerCallbackQueryAsync(queryId, $"⏳ Trial extended +{days} Day for {user.Username}!");
                        return;
                    }
                }
            }

            // 3. blk:{shortHwid} -> Block device
            if (data.StartsWith("blk:", StringComparison.OrdinalIgnoreCase))
            {
                var shortHwid = data.Substring(4);
                var device = await db.Devices.FirstOrDefaultAsync(d => d.HardwareId.StartsWith(shortHwid));
                if (device != null)
                {
                    device.IsBlocked = true;
                    device.BlockReason = "Blocked remotely via Telegram";
                    await db.SaveChangesAsync();
                    await telegram.AnswerCallbackQueryAsync(queryId, $"🚫 Device {device.HardwareId[..Math.Min(12, device.HardwareId.Length)]} BLOCKED!");
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Telegram Callback Handling Error] {ex.Message}");
        }
    }
}

