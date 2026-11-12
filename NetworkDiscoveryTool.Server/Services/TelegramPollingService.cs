using System;
using System.Collections.Concurrent;
using System.Linq;
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

    public static readonly ConcurrentDictionary<string, bool> ApprovedUsersCache = new(StringComparer.OrdinalIgnoreCase);
    public static readonly ConcurrentDictionary<string, bool> RejectedUsersCache = new(StringComparer.OrdinalIgnoreCase);

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
                            else if (update.TryGetProperty("message", out var msg))
                            {
                                await HandleTextMessageAsync(msg);
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
        var queryId = cb.TryGetProperty("id", out var qid) ? qid.GetString() ?? "" : "";
        var data = cb.TryGetProperty("data", out var d) ? d.GetString() ?? "" : "";

        try
        {
            using var scope = _services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ServerDbContext>();
            var telegram = scope.ServiceProvider.GetRequiredService<TelegramNotifier>();

            // 1. appr:{username}:{shortHwid} -> Approve user registration
            if (data.StartsWith("appr:", StringComparison.OrdinalIgnoreCase))
            {
                var parts = data.Split(':');
                if (parts.Length >= 2)
                {
                    var targetUsername = parts[1].Trim();
                    ApprovedUsersCache[targetUsername] = true;
                    RejectedUsersCache.TryRemove(targetUsername, out _);

                    var user = await db.Users.FirstOrDefaultAsync(u => u.Username.ToLower() == targetUsername.ToLower());
                    if (user != null)
                    {
                        user.IsApproved = true;
                        var baseDate = (user.SubscriptionExpiresAt.HasValue && user.SubscriptionExpiresAt.Value > DateTime.UtcNow)
                            ? user.SubscriptionExpiresAt.Value
                            : DateTime.UtcNow;

                        user.SubscriptionExpiresAt = baseDate.AddMonths(1);
                        user.Status = nameof(UserSubscriptionStatus.Subscribed);

                        // Unblock devices
                        var boundHwids = await db.UserDevices.Where(ud => ud.UserId == user.Id).Select(ud => ud.HardwareId).ToListAsync();
                        foreach (var hw in boundHwids)
                        {
                            var dev = await db.Devices.FirstOrDefaultAsync(dev => dev.HardwareId == hw);
                            if (dev != null) { dev.IsBlocked = false; dev.BlockReason = null; }
                        }
                        if (!string.IsNullOrWhiteSpace(user.LastLoginHwid))
                        {
                            var origDev = await db.Devices.FirstOrDefaultAsync(dev => dev.HardwareId == user.LastLoginHwid);
                            if (origDev != null) { origDev.IsBlocked = false; origDev.BlockReason = null; }
                        }

                        await db.SaveChangesAsync();
                        await telegram.AnswerCallbackQueryAsync(queryId, $"✅ {user.Username} APPROVED!");
                        await telegram.SendApprovalNotificationAsync(user.Username, "Approved (1 Month Subscribed)");
                        return;
                    }
                    else
                    {
                        await telegram.AnswerCallbackQueryAsync(queryId, $"✅ '{targetUsername}' approval cached!");
                        await telegram.SendApprovalNotificationAsync(targetUsername, "Approval token recorded for workstation");
                        return;
                    }
                }
            }

            // 2. rejc:{username}:{shortHwid} -> Reject user registration
            if (data.StartsWith("rejc:", StringComparison.OrdinalIgnoreCase))
            {
                var parts = data.Split(':');
                if (parts.Length >= 2)
                {
                    var targetUsername = parts[1].Trim();
                    RejectedUsersCache[targetUsername] = true;
                    ApprovedUsersCache.TryRemove(targetUsername, out _);

                    var user = await db.Users.FirstOrDefaultAsync(u => u.Username.ToLower() == targetUsername.ToLower());
                    if (user != null)
                    {
                        user.IsApproved = false;
                        user.Status = nameof(UserSubscriptionStatus.Suspended);
                        await db.SaveChangesAsync();
                    }

                    await telegram.AnswerCallbackQueryAsync(queryId, $"❌ '{targetUsername}' REJECTED.");
                    await telegram.SendRejectionNotificationAsync(targetUsername);
                    return;
                }
            }

            // 3. sub:{months}:{userId} -> Extend/Activate subscription
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
                        user.IsApproved = true;

                        ApprovedUsersCache[user.Username] = true;
                        RejectedUsersCache.TryRemove(user.Username, out _);

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

            // 4. ext:{days}:{userId} -> Extend 24h trial
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
                        user.IsApproved = true;

                        ApprovedUsersCache[user.Username] = true;
                        RejectedUsersCache.TryRemove(user.Username, out _);

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

            // 5. blk:{shortHwid} -> Block device
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

            // Default acknowledgement to avoid endless button loading spinner
            if (!string.IsNullOrWhiteSpace(queryId))
            {
                await telegram.AnswerCallbackQueryAsync(queryId, "Action acknowledged.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Telegram Callback Handling Error] {ex.Message}");
            if (!string.IsNullOrWhiteSpace(queryId))
            {
                try
                {
                    using var scope = _services.CreateScope();
                    var telegram = scope.ServiceProvider.GetRequiredService<TelegramNotifier>();
                    await telegram.AnswerCallbackQueryAsync(queryId, "Error processing action.");
                }
                catch { }
            }
        }
    }

    private async Task HandleTextMessageAsync(JsonElement msg)
    {
        try
        {
            var text = msg.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";
            if (string.IsNullOrWhiteSpace(text)) return;

            using var scope = _services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ServerDbContext>();
            var telegram = scope.ServiceProvider.GetRequiredService<TelegramNotifier>();

            // /approve {username}
            if (text.StartsWith("/approve", StringComparison.OrdinalIgnoreCase) || text.StartsWith("approve", StringComparison.OrdinalIgnoreCase))
            {
                var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2)
                {
                    var username = parts[1].Trim().TrimStart('@');
                    ApprovedUsersCache[username] = true;
                    RejectedUsersCache.TryRemove(username, out _);

                    var user = await db.Users.FirstOrDefaultAsync(u => u.Username.ToLower() == username.ToLower());
                    if (user != null)
                    {
                        user.IsApproved = true;
                        var baseDate = (user.SubscriptionExpiresAt.HasValue && user.SubscriptionExpiresAt.Value > DateTime.UtcNow)
                            ? user.SubscriptionExpiresAt.Value
                            : DateTime.UtcNow;
                        user.SubscriptionExpiresAt = baseDate.AddMonths(1);
                        user.Status = nameof(UserSubscriptionStatus.Subscribed);
                        await db.SaveChangesAsync();
                        await telegram.SendApprovalNotificationAsync(user.Username, "Approved (1 Month Subscribed via Chat Command)");
                    }
                    else
                    {
                        await telegram.SendApprovalNotificationAsync(username, "Approval cached for workstation");
                    }
                }
            }
            // /reject {username}
            else if (text.StartsWith("/reject", StringComparison.OrdinalIgnoreCase) || text.StartsWith("reject", StringComparison.OrdinalIgnoreCase))
            {
                var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2)
                {
                    var username = parts[1].Trim().TrimStart('@');
                    RejectedUsersCache[username] = true;
                    ApprovedUsersCache.TryRemove(username, out _);

                    var user = await db.Users.FirstOrDefaultAsync(u => u.Username.ToLower() == username.ToLower());
                    if (user != null)
                    {
                        user.IsApproved = false;
                        user.Status = nameof(UserSubscriptionStatus.Suspended);
                        await db.SaveChangesAsync();
                    }
                    await telegram.SendRejectionNotificationAsync(username);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Telegram Text Message Error] {ex.Message}");
        }
    }
}
