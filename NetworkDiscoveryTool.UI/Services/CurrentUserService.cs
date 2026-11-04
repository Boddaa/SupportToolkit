using System;

namespace NetworkDiscoveryTool.UI.Services;

public sealed class CurrentUserService
{
    public int? UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string SubscriptionStatus { get; set; } = "Subscribed";
    public DateTime? ExpiresAt { get; set; }
    public double? RemainingHours { get; set; }

    public bool IsLoggedIn => !string.IsNullOrEmpty(Username);
    public bool IsAdmin => string.Equals(Role, "Administrator", StringComparison.OrdinalIgnoreCase) || string.Equals(Role, "Admin", StringComparison.OrdinalIgnoreCase);
    public bool IsTrial => string.Equals(SubscriptionStatus, "Trial", StringComparison.OrdinalIgnoreCase);
    public bool IsSubscribed => string.Equals(SubscriptionStatus, "Subscribed", StringComparison.OrdinalIgnoreCase);

    public string SubscriptionBadgeText
    {
        get
        {
            if (IsAdmin) return "🛡️ System Admin";
            if (IsTrial)
            {
                if (RemainingHours.HasValue)
                {
                    int hours = (int)RemainingHours.Value;
                    int mins = (int)((RemainingHours.Value - hours) * 60);
                    return $"⭐ 24h Trial ({hours}h {mins}m left)";
                }
                return "⭐ 24h Free Trial";
            }
            if (IsSubscribed)
            {
                if (ExpiresAt.HasValue)
                {
                    var days = (int)(ExpiresAt.Value - DateTime.UtcNow).TotalDays;
                    return $"💎 Pro ({days} days left)";
                }
                return "💎 Pro License";
            }
            return "🔒 Expired";
        }
    }

    public event Action? UserChanged;

    public void SetUser(
        int? userId,
        string username,
        string role,
        string status = "Subscribed",
        DateTime? expiresAt = null,
        double? remainingHours = null)
    {
        UserId = userId;
        Username = username;
        Role = role;
        SubscriptionStatus = status;
        ExpiresAt = expiresAt;
        RemainingHours = remainingHours;
        UserChanged?.Invoke();
    }

    public void Clear()
    {
        UserId = null;
        Username = string.Empty;
        Role = string.Empty;
        SubscriptionStatus = "Subscribed";
        ExpiresAt = null;
        RemainingHours = null;
        UserChanged?.Invoke();
    }
}
