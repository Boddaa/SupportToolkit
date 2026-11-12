using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;

namespace NetworkDiscoveryTool.Services.Services;

public sealed class CloudAuthService
{
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(30) };
    
    public string ServerBaseUrl { get; set; } = "http://supporttoolkit.runasp.net";

    public sealed record CloudAuthResult(
        bool Success,
        string Message,
        string? Status = null,
        string? Role = null,
        DateTime? ExpiresAt = null,
        double? RemainingHours = null,
        int? UserId = null);

    public sealed record DeviceCheckResult(
        bool IsKnown,
        bool CanStartTrial,
        bool IsBlocked,
        string Message,
        DateTime? TrialExpiresAt);

    private sealed record RegisterPayload(
        string Username,
        string Password,
        string? Email,
        string HardwareId,
        string? MachineName);

    private sealed record LoginPayload(
        string Username,
        string Password,
        string HardwareId,
        string? MachineName);

    private sealed record DeviceCheckPayload(string HardwareId);

    #region Safe Json Extraction Helpers
    private static string? GetString(JsonElement element, string propName, string? fallback = null)
    {
        if (element.TryGetProperty(propName, out var prop))
        {
            if (prop.ValueKind == JsonValueKind.String)
                return prop.GetString();
        }
        return fallback;
    }

    private static bool GetBoolean(JsonElement element, string propName, bool fallback = false)
    {
        if (element.TryGetProperty(propName, out var prop))
        {
            if (prop.ValueKind == JsonValueKind.True) return true;
            if (prop.ValueKind == JsonValueKind.False) return false;
        }
        return fallback;
    }

    private static DateTime? GetDateTime(JsonElement element, string propName)
    {
        if (element.TryGetProperty(propName, out var prop) &&
            prop.ValueKind != JsonValueKind.Null &&
            prop.ValueKind != JsonValueKind.Undefined &&
            prop.TryGetDateTime(out var dt))
        {
            return dt;
        }
        return null;
    }

    private static double? GetDouble(JsonElement element, string propName)
    {
        if (element.TryGetProperty(propName, out var prop) &&
            prop.ValueKind == JsonValueKind.Number &&
            prop.TryGetDouble(out var d))
        {
            return d;
        }
        return null;
    }

    private static int? GetInt(JsonElement element, string propName)
    {
        if (element.TryGetProperty(propName, out var prop) &&
            prop.ValueKind == JsonValueKind.Number &&
            prop.TryGetInt32(out var i))
        {
            return i;
        }
        return null;
    }
    #endregion

    public async Task<DeviceCheckResult> CheckDeviceTrialStatusAsync()
    {
        try
        {
            var hwid = HardwareIdService.GetHardwareId();
            var url = $"{ServerBaseUrl.TrimEnd('/')}/api/device/check";
            var response = await HttpClient.PostAsJsonAsync(url, new DeviceCheckPayload(hwid)).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                var doc = await response.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
                return new DeviceCheckResult(
                    IsKnown: GetBoolean(doc, "isKnown"),
                    CanStartTrial: GetBoolean(doc, "canStartTrial"),
                    IsBlocked: GetBoolean(doc, "isBlocked"),
                    Message: GetString(doc, "message", "") ?? "",
                    TrialExpiresAt: GetDateTime(doc, "trialExpiresAt")
                );
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CloudAuth] Device precheck failed: {ex.Message}");
        }

        return new DeviceCheckResult(false, true, false, "Local evaluation", null);
    }

    public async Task<CloudAuthResult> RegisterAsync(string username, string password, string email = "")
    {
        try
        {
            var hwid = HardwareIdService.GetHardwareId();
            var url = $"{ServerBaseUrl.TrimEnd('/')}/api/auth/register";
            var payload = new RegisterPayload(username, password, email, hwid, Environment.MachineName);

            var response = await HttpClient.PostAsJsonAsync(url, payload).ConfigureAwait(false);
            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            bool success = GetBoolean(root, "success");
            string msg = GetString(root, "message", "") ?? "";
            string? status = GetString(root, "status");
            string? role = GetString(root, "role", "User");
            DateTime? expiresAt = GetDateTime(root, "expiresAt");
            double? remainingHours = GetDouble(root, "remainingHours");
            int? userId = GetInt(root, "userId");

            return new CloudAuthResult(success, msg, status, role, expiresAt, remainingHours, userId);
        }
        catch (Exception ex)
        {
            return new CloudAuthResult(false, $"Cloud server unreachable: {ex.Message}");
        }
    }

    public async Task<CloudAuthResult> LoginAsync(string username, string password)
    {
        try
        {
            var hwid = HardwareIdService.GetHardwareId();
            var url = $"{ServerBaseUrl.TrimEnd('/')}/api/auth/login";
            var payload = new LoginPayload(username, password, hwid, Environment.MachineName);

            var response = await HttpClient.PostAsJsonAsync(url, payload).ConfigureAwait(false);
            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            bool success = GetBoolean(root, "success");
            string msg = GetString(root, "message", "") ?? "";
            string? status = GetString(root, "status");
            string? role = GetString(root, "role", "User");
            DateTime? expiresAt = GetDateTime(root, "expiresAt");
            double? remainingHours = GetDouble(root, "remainingHours");
            int? userId = GetInt(root, "userId");

            return new CloudAuthResult(success, msg, status, role, expiresAt, remainingHours, userId);
        }
        catch (Exception ex)
        {
            return new CloudAuthResult(false, $"Cloud server connection error: {ex.Message}");
        }
    }

    public async Task<RemoteApprovalStatus> CheckApprovalStatusAsync(string username, string hardwareId)
    {
        try
        {
            var url = $"{ServerBaseUrl.TrimEnd('/')}/api/auth/check-approval?username={Uri.EscapeDataString(username)}&hardwareId={Uri.EscapeDataString(hardwareId)}";
            var response = await HttpClient.GetAsync(url).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return RemoteApprovalStatus.Pending;

            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            bool approved = GetBoolean(root, "approved");
            if (approved) return RemoteApprovalStatus.Approved;

            bool rejected = GetBoolean(root, "rejected");
            if (rejected) return RemoteApprovalStatus.Rejected;

            return RemoteApprovalStatus.Pending;
        }
        catch
        {
            return RemoteApprovalStatus.Pending;
        }
    }

    public sealed record FamilyMemberItem(
        int Id,
        string Username,
        string Email,
        string CreatedAt,
        string Status
    );

    public sealed record FamilyListResult(
        bool Success,
        bool IsSubscribed,
        int MaxSlots,
        int UsedSlots,
        int AvailableSlots,
        List<FamilyMemberItem> Members,
        string? Message = null
    );

    public async Task<FamilyListResult> GetFamilyMembersAsync(string parentUsername, int? parentUserId)
    {
        try
        {
            var url = $"{ServerBaseUrl.TrimEnd('/')}/api/user/family-members?parentUsername={Uri.EscapeDataString(parentUsername)}";
            if (parentUserId.HasValue) url += $"&parentUserId={parentUserId.Value}";

            var response = await HttpClient.GetAsync(url).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return new FamilyListResult(false, false, 2, 0, 0, new(), "Could not retrieve team members.");

            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            bool success = GetBoolean(root, "success");
            bool isSubscribed = GetBoolean(root, "isSubscribed");
            int maxSlots = GetInt(root, "maxSlots") ?? 2;
            int usedSlots = GetInt(root, "usedSlots") ?? 0;
            int availSlots = GetInt(root, "availableSlots") ?? 0;

            var list = new List<FamilyMemberItem>();
            if (root.TryGetProperty("members", out var memArr) && memArr.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in memArr.EnumerateArray())
                {
                    int id = GetInt(el, "id") ?? 0;
                    string un = GetString(el, "username", "") ?? "";
                    string em = GetString(el, "email", "") ?? "";
                    string ca = GetString(el, "createdAt", "") ?? "";
                    string st = GetString(el, "status", "") ?? "";
                    list.Add(new FamilyMemberItem(id, un, em, ca, st));
                }
            }

            return new FamilyListResult(success, isSubscribed, maxSlots, usedSlots, availSlots, list);
        }
        catch (Exception ex)
        {
            return new FamilyListResult(false, false, 2, 0, 0, new(), ex.Message);
        }
    }

    public async Task<(bool Success, string Message)> CreateFamilyMemberAsync(string parentUsername, int? parentUserId, string username, string password, string email = "")
    {
        try
        {
            var url = $"{ServerBaseUrl.TrimEnd('/')}/api/user/create-family-member";
            var payload = new
            {
                parentUsername = parentUsername,
                parentUserId = parentUserId,
                username = username.Trim(),
                password = password,
                email = email?.Trim() ?? ""
            };

            var response = await HttpClient.PostAsJsonAsync(url, payload).ConfigureAwait(false);
            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            bool success = GetBoolean(root, "success");
            string msg = GetString(root, "message", "Request failed.") ?? "Request failed.";
            return (success, msg);
        }
        catch (Exception ex)
        {
            return (false, $"Connection error: {ex.Message}");
        }
    }

    public async Task<(bool Success, string Message)> RemoveFamilyMemberAsync(string parentUsername, int? parentUserId, int subUserId)
    {
        try
        {
            var url = $"{ServerBaseUrl.TrimEnd('/')}/api/user/remove-family-member";
            var payload = new
            {
                parentUsername = parentUsername,
                parentUserId = parentUserId,
                subUserId = subUserId
            };

            var response = await HttpClient.PostAsJsonAsync(url, payload).ConfigureAwait(false);
            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            bool success = GetBoolean(root, "success");
            string msg = GetString(root, "message", "Request failed.") ?? "Request failed.";
            return (success, msg);
        }
        catch (Exception ex)
        {
            return (false, $"Connection error: {ex.Message}");
        }
    }
}
