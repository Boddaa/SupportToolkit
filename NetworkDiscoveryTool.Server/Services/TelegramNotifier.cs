using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace NetworkDiscoveryTool.Server.Services;

public sealed class TelegramNotifier
{
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(10) };
    private readonly IConfiguration _config;

    public TelegramNotifier(IConfiguration config)
    {
        _config = config;
    }

    public string BotToken => _config["Telegram:BotToken"] ?? "8914418594:AAGMMqY91qu0MnkEM453gjclAm_RyOyGxfc";
    public string AdminChatId => _config["Telegram:AdminChatId"] ?? "1119565273";

    public async Task<bool> SendTrialRegistrationAlertAsync(int userId, string username, string email, string hwid, string pcName)
    {
        if (string.IsNullOrWhiteSpace(BotToken) || string.IsNullOrWhiteSpace(AdminChatId))
            return false;

        try
        {
            var shortHwid = hwid.Length > 16 ? hwid[..16] : hwid;
            var text =
                $"<b>🚀 NEW USER &amp; 1-DAY TRIAL ACTIVATION</b>\n\n" +
                $"<b>👤 Username:</b> <code>{username}</code> (ID: {userId})\n" +
                $"<b>📧 Email:</b> {email}\n" +
                $"<b>💻 Computer:</b> {pcName}\n" +
                $"<b>🔑 HWID:</b> <code>{hwid}</code>\n" +
                $"<b>⏱️ Trial:</b> 24 Hours Active (UTC Server-Controlled)\n" +
                $"<b>📅 Started:</b> {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC\n\n" +
                $"<i>👇 Quick Admin Actions:</i>";

            var url = $"https://api.telegram.org/bot{BotToken}/sendMessage";

            var payload = new
            {
                chat_id = AdminChatId,
                text = text,
                parse_mode = "HTML",
                reply_markup = new
                {
                    inline_keyboard = new[]
                    {
                        new[]
                        {
                            new { text = "⭐ Activate 1 Month", callback_data = $"sub:1:{userId}" },
                            new { text = "⭐ 3 Months", callback_data = $"sub:3:{userId}" }
                        },
                        new[]
                        {
                            new { text = "⏳ +24h Trial", callback_data = $"ext:1:{userId}" },
                            new { text = "🚫 Block Device", callback_data = $"blk:{shortHwid}" }
                        }
                    }
                }
            };

            var json = JsonSerializer.Serialize(payload);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await HttpClient.PostAsync(url, content);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Telegram Error] {ex.Message}");
            return false;
        }
    }

    public async Task<bool> SendSubscriptionExtendedAlertAsync(string username, int months, DateTime newExpiry)
    {
        if (string.IsNullOrWhiteSpace(BotToken) || string.IsNullOrWhiteSpace(AdminChatId))
            return false;

        try
        {
            var text =
                $"<b>✅ SUBSCRIPTION ACTIVATED</b>\n\n" +
                $"<b>👤 User:</b> <code>{username}</code>\n" +
                $"<b>📅 Duration:</b> +{months} Month(s)\n" +
                $"<b>⏳ New Expiry:</b> {newExpiry:yyyy-MM-dd HH:mm:ss} UTC";

            var url = $"https://api.telegram.org/bot{BotToken}/sendMessage";
            var payload = new { chat_id = AdminChatId, text = text, parse_mode = "HTML" };
            var json = JsonSerializer.Serialize(payload);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await HttpClient.PostAsync(url, content);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> AnswerCallbackQueryAsync(string callbackQueryId, string text)
    {
        try
        {
            var url = $"https://api.telegram.org/bot{BotToken}/answerCallbackQuery";
            var payload = new { callback_query_id = callbackQueryId, text = text, show_alert = true };
            var json = JsonSerializer.Serialize(payload);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await HttpClient.PostAsync(url, content);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}

