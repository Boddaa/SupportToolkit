using System;
using System.ComponentModel.DataAnnotations;

namespace NetworkDiscoveryTool.Server.Models;

public enum UserSubscriptionStatus
{
    Trial,
    Subscribed,
    Expired,
    Suspended
}

public class ServerUser
{
    public int Id { get; set; }

    [Required, MaxLength(100)]
    public string Username { get; set; } = string.Empty;

    [MaxLength(200)]
    public string Email { get; set; } = string.Empty;

    [Required]
    public string PasswordHash { get; set; } = string.Empty;

    [MaxLength(50)]
    public string Role { get; set; } = "User"; // "Admin", "User"

    public bool IsApproved { get; set; } = true;

    [MaxLength(30)]
    public string Status { get; set; } = nameof(UserSubscriptionStatus.Trial);

    public DateTime? TrialStartedAt { get; set; }

    public DateTime? TrialExpiresAt { get; set; }

    public DateTime? SubscriptionExpiresAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? LastLoginAt { get; set; }

    [MaxLength(100)]
    public string? LastLoginHwid { get; set; }

    [MaxLength(50)]
    public string? LastLoginIp { get; set; }

    [MaxLength(500)]
    public string? Notes { get; set; }

    // Multi-Device / Family Subscription Support
    public int MaxDevices { get; set; } = 3;

    public int? ParentUserId { get; set; }
}

public class DeviceRegistration
{
    [Key, MaxLength(100)]
    public string HardwareId { get; set; } = string.Empty;

    [MaxLength(100)]
    public string MachineName { get; set; } = string.Empty;

    public int? AssociatedUserId { get; set; }

    public DateTime FirstSeenAt { get; set; } = DateTime.UtcNow;

    public DateTime TrialExpiresAt { get; set; }

    public bool IsTrialConsumed { get; set; } = false;

    public bool IsBlocked { get; set; } = false;

    [MaxLength(255)]
    public string? BlockReason { get; set; }

    public DateTime? LastSeenAt { get; set; }
}

public class UserDevice
{
    public int Id { get; set; }

    public int UserId { get; set; }

    [MaxLength(100)]
    public string HardwareId { get; set; } = string.Empty;

    [MaxLength(100)]
    public string MachineName { get; set; } = string.Empty;

    public DateTime BoundAt { get; set; } = DateTime.UtcNow;

    public DateTime LastUsedAt { get; set; } = DateTime.UtcNow;

    public bool IsActive { get; set; } = true;
}

public class AuditLog
{
    public int Id { get; set; }
    public string Action { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

public class AppRelease
{
    public int Id { get; set; }
    [Required, MaxLength(50)]
    public string Version { get; set; } = "2.5.0";
    public string ReleaseNotes { get; set; } = string.Empty;
    [Required, MaxLength(500)]
    public string DownloadUrl { get; set; } = "/downloads/SupportToolKit.exe";
    public bool IsMandatory { get; set; } = false;
    public long FileSize { get; set; } = 0;
    public string? FileHash { get; set; }
    public DateTime PublishedAt { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; } = true;
}

// Request and Response DTOs
public sealed record RegisterRequest(
    string Username,
    string Password,
    string? Email,
    string HardwareId,
    string? MachineName
);

public sealed record LoginRequest(
    string Username,
    string Password,
    string HardwareId,
    string? MachineName
);

public sealed record DeviceCheckRequest(
    string HardwareId
);

public sealed record AuthResponse(
    bool Success,
    string Message,
    string? Status = null,
    string? Role = null,
    DateTime? ExpiresAt = null,
    double? RemainingHours = null,
    string? Token = null,
    int? UserId = null
);

public sealed record SubscriptionUpdateRequest(
    int UserId,
    int AddMonths,
    string? Status = null
);

public sealed record BlockDeviceRequest(
    string HardwareId,
    bool Block,
    string? Reason = null
);

public sealed record CreateFamilyMemberRequest(
    int ParentUserId,
    string Username,
    string Password,
    string? Email
);

public sealed record UnlinkDeviceRequest(
    int UserId,
    string HardwareId
);

public sealed record CheckUpdateResponse(
    bool HasUpdate,
    string LatestVersion,
    string CurrentVersion,
    string DownloadUrl,
    string ReleaseNotes,
    bool IsMandatory,
    long FileSize,
    string PublishedAt
);

public sealed record PublishReleaseRequest(
    string Version,
    string ReleaseNotes,
    string DownloadUrl,
    bool IsMandatory
);
