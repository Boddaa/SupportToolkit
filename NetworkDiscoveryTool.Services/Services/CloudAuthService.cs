using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;

namespace NetworkDiscoveryTool.Services.Services;

public sealed class CloudAuthService
{
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(8) };
    
    public string ServerBaseUrl { get; set; } = "http://localhost:5000";

    public sealed record CloudAuthResult(
        bool Success,
        string Message,
        string? Status = null,
        string? Role = null,
        DateTime? ExpiresAt = null,
        double? RemainingHours = null);

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
            var response = await HttpClient.PostAsJsonAsync(url, new DeviceCheckPayload(hwid));
            if (response.IsSuccessStatusCode)
            {
                var doc = await response.Content.ReadFromJsonAsync<JsonElement>();
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

            var response = await HttpClient.PostAsJsonAsync(url, payload);
            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            bool success = root.TryGetProperty("success", out var s) && s.GetBoolean();
            string msg = root.TryGetProperty("message", out var m) ? m.GetString() ?? "" : "";
            string? status = root.TryGetProperty("status", out var st) ? st.GetString() : null;
            string? role = root.TryGetProperty("role", out var r) ? r.GetString() : "User";
            DateTime? expiresAt = root.TryGetProperty("expiresAt", out var ea) && ea.TryGetDateTime(out var dt) ? dt : null;
            double? remainingHours = root.TryGetProperty("remainingHours", out var rh) && rh.TryGetDouble(out var d) ? d : null;

            return new CloudAuthResult(success, msg, status, role, expiresAt, remainingHours);
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

            var response = await HttpClient.PostAsJsonAsync(url, payload);
            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            bool success = root.TryGetProperty("success", out var s) && s.GetBoolean();
            string msg = root.TryGetProperty("message", out var m) ? m.GetString() ?? "" : "";
            string? status = root.TryGetProperty("status", out var st) ? st.GetString() : null;
            string? role = root.TryGetProperty("role", out var r) ? r.GetString() : "User";
            DateTime? expiresAt = root.TryGetProperty("expiresAt", out var ea) && ea.TryGetDateTime(out var dt) ? dt : null;
            double? remainingHours = root.TryGetProperty("remainingHours", out var rh) && rh.TryGetDouble(out var d) ? d : null;

            return new CloudAuthResult(success, msg, status, role, expiresAt, remainingHours);
        }
        catch (Exception ex)
        {
            return new CloudAuthResult(false, $"Cloud server connection error: {ex.Message}");
        }
    }
}

