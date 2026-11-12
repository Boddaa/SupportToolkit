using System;
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
                    IsKnown: doc.TryGetProperty("isKnown", out var ik) && ik.GetBoolean(),
                    CanStartTrial: doc.TryGetProperty("canStartTrial", out var cst) && cst.GetBoolean(),
                    IsBlocked: doc.TryGetProperty("isBlocked", out var ib) && ib.GetBoolean(),
                    Message: doc.TryGetProperty("message", out var m) ? m.GetString() ?? "" : "",
                    TrialExpiresAt: doc.TryGetProperty("trialExpiresAt", out var tea) && tea.TryGetDateTime(out var dt) ? dt : null
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

            bool success = root.TryGetProperty("success", out var s) && s.GetBoolean();
            string msg = root.TryGetProperty("message", out var m) ? m.GetString() ?? "" : "";
            string? status = root.TryGetProperty("status", out var st) ? st.GetString() : null;
            string? role = root.TryGetProperty("role", out var r) ? r.GetString() : "User";
            DateTime? expiresAt = root.TryGetProperty("expiresAt", out var ea) && ea.TryGetDateTime(out var dt) ? dt : null;
            double? remainingHours = root.TryGetProperty("remainingHours", out var rh) && rh.TryGetDouble(out var d) ? d : null;
            int? userId = root.TryGetProperty("userId", out var uid) && uid.TryGetInt32(out var idVal) ? idVal : null;

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

            bool success = root.TryGetProperty("success", out var s) && s.GetBoolean();
            string msg = root.TryGetProperty("message", out var m) ? m.GetString() ?? "" : "";
            string? status = root.TryGetProperty("status", out var st) ? st.GetString() : null;
            string? role = root.TryGetProperty("role", out var r) ? r.GetString() : "User";
            DateTime? expiresAt = root.TryGetProperty("expiresAt", out var ea) && ea.TryGetDateTime(out var dt) ? dt : null;
            double? remainingHours = root.TryGetProperty("remainingHours", out var rh) && rh.TryGetDouble(out var d) ? d : null;
            int? userId = root.TryGetProperty("userId", out var uid) && uid.TryGetInt32(out var idVal) ? idVal : null;

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

            bool approved = root.TryGetProperty("approved", out var a) && a.GetBoolean();
            if (approved) return RemoteApprovalStatus.Approved;

            bool rejected = root.TryGetProperty("rejected", out var r) && r.GetBoolean();
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

            bool success = root.TryGetProperty("success", out var s) && s.GetBoolean();
            bool isSubscribed = root.TryGetProperty("isSubscribed", out var isSub) && isSub.GetBoolean();
            int maxSlots = root.TryGetProperty("maxSlots", out var ms) ? ms.GetInt32() : 2;
            int usedSlots = root.TryGetProperty("usedSlots", out var us) ? us.GetInt32() : 0;
            int availSlots = root.TryGetProperty("availableSlots", out var av) ? av.GetInt32() : 0;

            var list = new List<FamilyMemberItem>();
            if (root.TryGetProperty("members", out var memArr) && memArr.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in memArr.EnumerateArray())
                {
                    int id = el.TryGetProperty("id", out var idProp) ? idProp.GetInt32() : 0;
                    string un = el.TryGetProperty("username", out var unProp) ? unProp.GetString() ?? "" : "";
                    string em = el.TryGetProperty("email", out var emProp) ? emProp.GetString() ?? "" : "";
                    string ca = el.TryGetProperty("createdAt", out var caProp) ? caProp.GetString() ?? "" : "";
                    string st = el.TryGetProperty("status", out var stProp) ? stProp.GetString() ?? "" : "";
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

            bool success = root.TryGetProperty("success", out var s) && s.GetBoolean();
            string msg = root.TryGetProperty("message", out var m) ? m.GetString() ?? "" : "Request failed.";
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

            bool success = root.TryGetProperty("success", out var s) && s.GetBoolean();
            string msg = root.TryGetProperty("message", out var m) ? m.GetString() ?? "" : "Request failed.";
            return (success, msg);
        }
        catch (Exception ex)
        {
            return (false, $"Connection error: {ex.Message}");
        }
    }
}
