using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace NetworkDiscoveryTool.Services.Services;

public enum RemoteApprovalStatus
{
    Pending,
    Approved,
    Rejected
}

public sealed class TelegramNotificationService
{
    private static readonly HttpClient HttpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

    // =========================================================================
    // DEFAULT TELEGRAM BOT INTEGRATION CONFIGURATION
    // You can update these default credentials here at any time before publishing
    // =========================================================================
    public const string DefaultBotToken = "8914418594:AAGMMqY91qu0MnkEM453gjclAm_RyOyGxfc";
    public const string DefaultAdminChatId = "1119565273";

    public string BotToken { get; set; } = DefaultBotToken;
    public string AdminChatId { get; set; } = DefaultAdminChatId;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(BotToken) && !string.IsNullOrWhiteSpace(AdminChatId);

    public async Task<bool> SendRegistrationAlertAsync(string username, string email, string hardwareId, string pcName)
    {
        if (!IsConfigured)
            return false;

        try
        {
            var shortHwid = hardwareId.Length > 12 ? hardwareId.Substring(0, 12) : hardwareId;
            var text = $"<b>📌 NEW OPERATOR REGISTRATION REQUEST</b>\n\n" +
                       $"<b>👤 Username:</b> <code>{username}</code>\n" +
                       $"<b>📧 Email:</b> {email}\n" +
                       $"<b>💻 Computer:</b> {pcName}\n" +
                       $"<b>🔑 HWID:</b> <code>{hardwareId}</code>\n" +
                       $"<b>📅 Date:</b> {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n\n" +
                       $"<i>👇 Tap a button below to authorize or reject this console session:</i>";

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
                            new { text = "✅ Approve Operator", callback_data = $"appr:{username}:{shortHwid}" },
                            new { text = "❌ Reject", callback_data = $"rejc:{username}:{shortHwid}" }
                        }
                    }
                }
            };

            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await HttpClient.PostAsync(url, content);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<RemoteApprovalStatus> CheckApprovalStatusAsync(string username, string hardwareId)
    {
        if (!IsConfigured || string.IsNullOrWhiteSpace(username))
            return RemoteApprovalStatus.Pending;

        try
        {
            var url = $"https://api.telegram.org/bot{BotToken}/getUpdates?limit=50&timeout=0";
            var response = await HttpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode)
                return RemoteApprovalStatus.Pending;

            var jsonString = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(jsonString);
            if (!doc.RootElement.TryGetProperty("result", out var resultElement) || resultElement.ValueKind != JsonValueKind.Array)
                return RemoteApprovalStatus.Pending;

            var targetApprove = $"appr:{username.Trim()}";
            var targetReject = $"rejc:{username.Trim()}";

            foreach (var update in resultElement.EnumerateArray())
            {
                // 1. Check Callback Queries (Button clicks)
                if (update.TryGetProperty("callback_query", out var callbackQuery))
                {
                    if (callbackQuery.TryGetProperty("from", out var fromUser) &&
                        fromUser.TryGetProperty("id", out var fromId))
                    {
                        var senderId = fromId.ToString();
                        if (string.Equals(senderId, AdminChatId.Trim(), StringComparison.OrdinalIgnoreCase))
                        {
                            if (callbackQuery.TryGetProperty("data", out var dataProp))
                            {
                                var data = dataProp.GetString() ?? "";
                                if (data.StartsWith(targetApprove, StringComparison.OrdinalIgnoreCase))
                                {
                                    // Acknowledge callback query
                                    if (callbackQuery.TryGetProperty("id", out var queryId))
                                        _ = AnswerCallbackQueryAsync(queryId.GetString(), "✅ User Approved!");

                                    return RemoteApprovalStatus.Approved;
                                }
                                if (data.StartsWith(targetReject, StringComparison.OrdinalIgnoreCase))
                                {
                                    if (callbackQuery.TryGetProperty("id", out var queryId))
                                        _ = AnswerCallbackQueryAsync(queryId.GetString(), "❌ User Rejected.");

                                    return RemoteApprovalStatus.Rejected;
                                }
                            }
                        }
                    }
                }

                // 2. Check Text Messages (e.g. "/approve username" or "approve username")
                if (update.TryGetProperty("message", out var msg))
                {
                    if (msg.TryGetProperty("from", out var fromUser) &&
                        fromUser.TryGetProperty("id", out var fromId))
                    {
                        var senderId = fromId.ToString();
                        if (string.Equals(senderId, AdminChatId.Trim(), StringComparison.OrdinalIgnoreCase))
                        {
                            if (msg.TryGetProperty("text", out var textProp))
                            {
                                var text = textProp.GetString() ?? "";
                                if (text.Contains($"/approve {username}", StringComparison.OrdinalIgnoreCase) ||
                                    text.Contains($"/approve_{username}", StringComparison.OrdinalIgnoreCase))
                                {
                                    return RemoteApprovalStatus.Approved;
                                }
                                if (text.Contains($"/reject {username}", StringComparison.OrdinalIgnoreCase) ||
                                    text.Contains($"/reject_{username}", StringComparison.OrdinalIgnoreCase))
                                {
                                    return RemoteApprovalStatus.Rejected;
                                }
                            }
                        }
                    }
                }
            }
        }
        catch
        {
            // Suppress transient network exceptions
        }

        return RemoteApprovalStatus.Pending;
    }

    private async Task AnswerCallbackQueryAsync(string? callbackQueryId, string text)
    {
        if (string.IsNullOrWhiteSpace(callbackQueryId) || !IsConfigured) return;
        try
        {
            var url = $"https://api.telegram.org/bot{BotToken}/answerCallbackQuery";
            var payload = new { callback_query_id = callbackQueryId, text = text };
            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            await HttpClient.PostAsync(url, content);
        }
        catch { }
    }
}
