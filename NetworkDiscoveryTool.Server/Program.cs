using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NetworkDiscoveryTool.Server.Data;
using NetworkDiscoveryTool.Server.Models;
using NetworkDiscoveryTool.Server.Services;

var builder = WebApplication.CreateBuilder(args);

// Configure Database: Use Supabase PostgreSQL if connection string is configured, otherwise fallback to SQLite
var pgConn = builder.Configuration.GetConnectionString("Supabase")
          ?? builder.Configuration.GetConnectionString("DefaultConnection")
          ?? Environment.GetEnvironmentVariable("SUPABASE_CONNECTION_STRING");

if (!string.IsNullOrWhiteSpace(pgConn))
{
    Console.WriteLine("[Database] Connecting to Supabase (PostgreSQL)...");
    builder.Services.AddDbContext<ServerDbContext>(options =>
        options.UseNpgsql(pgConn));
}
else
{
    Console.WriteLine("[Database] Using local SQLite database...");
    var dbPath = Path.Combine(AppContext.BaseDirectory, "cloud_licensing.db");
    builder.Services.AddDbContext<ServerDbContext>(options =>
        options.UseSqlite($"Data Source={dbPath}"));
}

builder.Services.AddSingleton<TelegramNotifier>();
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
    });
});

var app = builder.Build();

app.UseCors();
app.UseDefaultFiles();
app.UseStaticFiles();

// Initialize Database & Seed Default Admin
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ServerDbContext>();
    
    if (db.Database.IsNpgsql())
    {
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS "Users" (
                "Id" SERIAL PRIMARY KEY,
                "Username" VARCHAR(100) NOT NULL UNIQUE,
                "Email" VARCHAR(200),
                "PasswordHash" TEXT NOT NULL,
                "Role" VARCHAR(50) NOT NULL DEFAULT 'User',
                "IsApproved" BOOLEAN NOT NULL DEFAULT TRUE,
                "Status" VARCHAR(30) NOT NULL DEFAULT 'Trial',
                "TrialStartedAt" TIMESTAMP WITH TIME ZONE,
                "TrialExpiresAt" TIMESTAMP WITH TIME ZONE,
                "SubscriptionExpiresAt" TIMESTAMP WITH TIME ZONE,
                "CreatedAt" TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
                "LastLoginAt" TIMESTAMP WITH TIME ZONE,
                "LastLoginHwid" VARCHAR(100),
                "LastLoginIp" VARCHAR(50),
                "Notes" VARCHAR(500)
            );

            CREATE TABLE IF NOT EXISTS "Devices" (
                "HardwareId" VARCHAR(100) PRIMARY KEY,
                "MachineName" VARCHAR(100),
                "AssociatedUserId" INTEGER,
                "FirstSeenAt" TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
                "TrialExpiresAt" TIMESTAMP WITH TIME ZONE,
                "IsTrialConsumed" BOOLEAN NOT NULL DEFAULT FALSE,
                "IsBlocked" BOOLEAN NOT NULL DEFAULT FALSE,
                "BlockReason" VARCHAR(255),
                "LastSeenAt" TIMESTAMP WITH TIME ZONE
            );

            CREATE TABLE IF NOT EXISTS "AuditLogs" (
                "Id" SERIAL PRIMARY KEY,
                "Action" VARCHAR(100) NOT NULL,
                "Details" TEXT,
                "Timestamp" TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP
            );
        """);
    }
    else
    {
        await db.Database.EnsureCreatedAsync();
    }

    if (!await db.Users.AnyAsync(u => u.Role == "Admin"))
    {
        var defaultAdmin = new ServerUser
        {
            Username = "admin",
            PasswordHash = PasswordSecurity.HashPassword("159357"),
            Email = "admin@supporttoolkit.local",
            Role = "Admin",
            IsApproved = true,
            Status = nameof(UserSubscriptionStatus.Subscribed),
            SubscriptionExpiresAt = DateTime.UtcNow.AddYears(10),
            CreatedAt = DateTime.UtcNow
        };
        db.Users.Add(defaultAdmin);
        await db.SaveChangesAsync();
    }
}

// =========================================================================
// AUTHENTICATION & TRIAL LOGIC ENDPOINTS
// =========================================================================

// 1. Device Pre-Check (Anti-Reinstall Check)
app.MapPost("/api/device/check", async ([FromBody] DeviceCheckRequest req, ServerDbContext db) =>
{
    if (string.IsNullOrWhiteSpace(req.HardwareId))
        return Results.BadRequest(new { error = "HardwareId required" });

    var device = await db.Devices.FirstOrDefaultAsync(d => d.HardwareId == req.HardwareId.Trim());
    if (device == null)
    {
        return Results.Ok(new
        {
            IsKnown = false,
            CanStartTrial = true,
            IsBlocked = false,
            Message = "Device eligible for 24-Hour Free Trial"
        });
    }

    if (device.IsBlocked)
    {
        return Results.Ok(new
        {
            IsKnown = true,
            CanStartTrial = false,
            IsBlocked = true,
            Message = device.BlockReason ?? "Device is administratively blocked"
        });
    }

    bool isTrialExpired = device.IsTrialConsumed || device.TrialExpiresAt <= DateTime.UtcNow;
    return Results.Ok(new
    {
        IsKnown = true,
        CanStartTrial = !isTrialExpired,
        IsBlocked = false,
        TrialExpiresAt = device.TrialExpiresAt,
        IsTrialConsumed = device.IsTrialConsumed,
        Message = isTrialExpired ? "This device has already consumed its 1-day free trial." : "Trial active."
    });
});

// 2. User Registration (With Strict HWID 1-Day Trial Binding)
app.MapPost("/api/auth/register", async (
    [FromBody] RegisterRequest req,
    ServerDbContext db,
    TelegramNotifier telegram,
    HttpContext http) =>
{
    if (string.IsNullOrWhiteSpace(req.Username) || string.IsNullOrWhiteSpace(req.Password))
        return Results.BadRequest(new AuthResponse(false, "Username and password are required."));

    if (string.IsNullOrWhiteSpace(req.HardwareId))
        return Results.BadRequest(new AuthResponse(false, "Device Hardware ID is required."));

    var username = req.Username.Trim();
    if (await db.Users.AnyAsync(u => u.Username.ToLower() == username.ToLower()))
        return Results.Conflict(new AuthResponse(false, "Username is already registered."));

    var hwid = req.HardwareId.Trim();
    var existingDevice = await db.Devices.FirstOrDefaultAsync(d => d.HardwareId == hwid);

    if (existingDevice != null && existingDevice.IsBlocked)
        return Results.Json(new AuthResponse(false, "This device has been administratively blocked."), statusCode: 403);

    // ANTI-CHEAT: Check if this machine already consumed the trial
    bool deviceConsumedTrial = existingDevice != null && (existingDevice.IsTrialConsumed || existingDevice.TrialExpiresAt <= DateTime.UtcNow);

    var user = new ServerUser
    {
        Username = username,
        Email = req.Email?.Trim() ?? string.Empty,
        PasswordHash = PasswordSecurity.HashPassword(req.Password),
        Role = "User",
        CreatedAt = DateTime.UtcNow,
        LastLoginHwid = hwid,
        LastLoginIp = http.Connection.RemoteIpAddress?.ToString()
    };

    if (existingDevice != null)
    {
        // Device was seen before!
        if (existingDevice.IsTrialConsumed || existingDevice.TrialExpiresAt <= DateTime.UtcNow)
        {
            // Trial has already ended on this machine!
            user.Status = nameof(UserSubscriptionStatus.Expired);
            user.TrialExpiresAt = existingDevice.TrialExpiresAt;
            user.Notes = "Device already consumed trial previously.";
        }
        else
        {
            // Trial is still ticking from when the device was first seen.
            // Inherit remaining time; do NOT reset back to 24h!
            user.Status = nameof(UserSubscriptionStatus.Trial);
            user.TrialStartedAt = existingDevice.FirstSeenAt;
            user.TrialExpiresAt = existingDevice.TrialExpiresAt;
        }
    }
    else
    {
        // Brand new device -> Grant exactly 24-Hour Free Trial
        var trialEnd = DateTime.UtcNow.AddDays(1);
        user.Status = nameof(UserSubscriptionStatus.Trial);
        user.TrialStartedAt = DateTime.UtcNow;
        user.TrialExpiresAt = trialEnd;

        existingDevice = new DeviceRegistration
        {
            HardwareId = hwid,
            MachineName = req.MachineName?.Trim() ?? "Unknown PC",
            FirstSeenAt = DateTime.UtcNow,
            TrialExpiresAt = trialEnd,
            IsTrialConsumed = false,
            LastSeenAt = DateTime.UtcNow
        };
        db.Devices.Add(existingDevice);
    }

    db.Users.Add(user);
    await db.SaveChangesAsync();

    // Link device to user
    if (existingDevice != null)
    {
        existingDevice.AssociatedUserId = user.Id;
        await db.SaveChangesAsync();
    }

    // Trigger Telegram notification to Admin
    _ = Task.Run(async () =>
    {
        await telegram.SendTrialRegistrationAlertAsync(
            user.Id,
            user.Username,
            string.IsNullOrWhiteSpace(user.Email) ? "None" : user.Email,
            hwid,
            req.MachineName ?? "PC");
    });

    if (deviceConsumedTrial)
    {
        return Results.Ok(new AuthResponse(
            Success: false,
            Message: "This device has already used its 1-day free trial. Contact admin to subscribe.",
            Status: nameof(UserSubscriptionStatus.Expired)
        ));
    }

    var remainingHours = (user.TrialExpiresAt!.Value - DateTime.UtcNow).TotalHours;
    return Results.Ok(new AuthResponse(
        Success: true,
        Message: "Account created! 24-Hour Free Trial is now active.",
        Status: nameof(UserSubscriptionStatus.Trial),
        Role: user.Role,
        ExpiresAt: user.TrialExpiresAt,
        RemainingHours: Math.Round(remainingHours, 1)
    ));
});

// 3. User Login (Time-Validated via UTC Server Clock)
app.MapPost("/api/auth/login", async (
    [FromBody] LoginRequest req,
    ServerDbContext db,
    HttpContext http) =>
{
    if (string.IsNullOrWhiteSpace(req.Username) || string.IsNullOrWhiteSpace(req.Password))
        return Results.BadRequest(new AuthResponse(false, "Username and password required."));

    var username = req.Username.Trim();
    var user = await db.Users.FirstOrDefaultAsync(u => u.Username.ToLower() == username.ToLower());
    if (user == null || !PasswordSecurity.VerifyPassword(req.Password, user.PasswordHash))
        return Results.Json(new AuthResponse(false, "Invalid username or password."), statusCode: 401);

    var hwid = req.HardwareId?.Trim();
    if (!string.IsNullOrWhiteSpace(hwid))
    {
        var device = await db.Devices.FirstOrDefaultAsync(d => d.HardwareId == hwid);
        if (device != null && device.IsBlocked)
        {
            return Results.Json(new AuthResponse(false, "This computer has been blocked. Contact administrator."), statusCode: 403);
        }
    }

    var now = DateTime.UtcNow;

    // A. Active Paid Subscription
    if (user.Status == nameof(UserSubscriptionStatus.Subscribed))
    {
        if (user.SubscriptionExpiresAt.HasValue && user.SubscriptionExpiresAt.Value > now)
        {
            user.LastLoginAt = now;
            user.LastLoginHwid = hwid;
            user.LastLoginIp = http.Connection.RemoteIpAddress?.ToString();
            await db.SaveChangesAsync();

            var hours = (user.SubscriptionExpiresAt.Value - now).TotalHours;
            return Results.Ok(new AuthResponse(
                Success: true,
                Message: "Login successful (Active Subscription).",
                Status: user.Status,
                Role: user.Role,
                ExpiresAt: user.SubscriptionExpiresAt,
                RemainingHours: Math.Round(hours, 1)
            ));
        }

        // Subscription Expired
        user.Status = nameof(UserSubscriptionStatus.Expired);
        await db.SaveChangesAsync();
        return Results.Json(new AuthResponse(
            Success: false,
            Message: $"Your subscription expired on {user.SubscriptionExpiresAt:yyyy-MM-dd}. Please renew to continue.",
            Status: nameof(UserSubscriptionStatus.Expired)
        ), statusCode: 403);
    }

    // B. Free Trial (24 Hours)
    if (user.Status == nameof(UserSubscriptionStatus.Trial))
    {
        if (user.TrialExpiresAt.HasValue && user.TrialExpiresAt.Value > now)
        {
            user.LastLoginAt = now;
            user.LastLoginHwid = hwid;
            user.LastLoginIp = http.Connection.RemoteIpAddress?.ToString();
            await db.SaveChangesAsync();

            var hours = (user.TrialExpiresAt.Value - now).TotalHours;
            return Results.Ok(new AuthResponse(
                Success: true,
                Message: "Login successful (Free Trial).",
                Status: user.Status,
                Role: user.Role,
                ExpiresAt: user.TrialExpiresAt,
                RemainingHours: Math.Round(hours, 1)
            ));
        }

        // Trial Expired -> Mark device and user as consumed
        user.Status = nameof(UserSubscriptionStatus.Expired);
        if (!string.IsNullOrWhiteSpace(hwid))
        {
            var dev = await db.Devices.FirstOrDefaultAsync(d => d.HardwareId == hwid);
            if (dev != null) dev.IsTrialConsumed = true;
        }
        await db.SaveChangesAsync();

        return Results.Json(new AuthResponse(
            Success: false,
            Message: "Your 1-day free trial has expired. Please subscribe to unlock full access.",
            Status: nameof(UserSubscriptionStatus.Expired)
        ), statusCode: 403);
    }

    // C. Expired or Suspended
    return Results.Json(new AuthResponse(
        Success: false,
        Message: "Account access expired or suspended. Contact administrator on Telegram.",
        Status: user.Status
    ), statusCode: 403);
});

// =========================================================================
// TELEGRAM WEBHOOK (INSTANT APPROVAL & EXTENSIONS)
// =========================================================================
app.MapPost("/api/telegram/webhook", async (
    [FromBody] JsonElement update,
    ServerDbContext db,
    TelegramNotifier telegram) =>
{
    try
    {
        if (update.TryGetProperty("callback_query", out var callbackQuery))
        {
            var queryId = callbackQuery.GetProperty("id").GetString() ?? "";
            var data = callbackQuery.GetProperty("data").GetString() ?? "";

            // Format 1: sub:{months}:{userId} -> Activate subscription
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
                        await db.SaveChangesAsync();

                        await telegram.AnswerCallbackQueryAsync(queryId, $"✅ {user.Username} activated for {months} Month(s)!");
                        await telegram.SendSubscriptionExtendedAlertAsync(user.Username, months, user.SubscriptionExpiresAt.Value);
                        return Results.Ok();
                    }
                }
            }

            // Format 2: ext:{days}:{userId} -> Extend trial
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
                            }
                        }

                        await db.SaveChangesAsync();
                        await telegram.AnswerCallbackQueryAsync(queryId, $"⏳ Trial extended +{days} Day for {user.Username}!");
                        return Results.Ok();
                    }
                }
            }

            // Format 3: blk:{shortHwid} -> Block device
            if (data.StartsWith("blk:", StringComparison.OrdinalIgnoreCase))
            {
                var shortHwid = data.Substring(4);
                var device = await db.Devices.FirstOrDefaultAsync(d => d.HardwareId.StartsWith(shortHwid));
                if (device != null)
                {
                    device.IsBlocked = true;
                    device.BlockReason = "Blocked remotely via Telegram";
                    await db.SaveChangesAsync();
                    await telegram.AnswerCallbackQueryAsync(queryId, $"🚫 Device {device.HardwareId[..8]} BLOCKED!");
                    return Results.Ok();
                }
            }
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[Webhook Error] {ex.Message}");
    }

    return Results.Ok();
});

// =========================================================================
// ADMIN DASHBOARD API ENDPOINTS
// =========================================================================
app.MapGet("/api/admin/dashboard-stats", async (ServerDbContext db) =>
{
    var now = DateTime.UtcNow;
    var users = await db.Users.OrderByDescending(u => u.CreatedAt).ToListAsync();
    var devicesCount = await db.Devices.CountAsync();

    int activeSubscribers = users.Count(u => u.Status == nameof(UserSubscriptionStatus.Subscribed) && u.SubscriptionExpiresAt > now);
    int activeTrials = users.Count(u => u.Status == nameof(UserSubscriptionStatus.Trial) && u.TrialExpiresAt > now);
    int expiredCount = users.Count(u => u.Status == nameof(UserSubscriptionStatus.Expired) ||
                                       (u.Status == nameof(UserSubscriptionStatus.Trial) && u.TrialExpiresAt <= now) ||
                                       (u.Status == nameof(UserSubscriptionStatus.Subscribed) && u.SubscriptionExpiresAt <= now));

    var userList = users.Select(u =>
    {
        string remainingText = "Expired";
        string expiresAtText = "N/A";

        if (u.Status == nameof(UserSubscriptionStatus.Subscribed) && u.SubscriptionExpiresAt.HasValue)
        {
            expiresAtText = u.SubscriptionExpiresAt.Value.ToString("yyyy-MM-dd HH:mm UTC");
            if (u.SubscriptionExpiresAt.Value > now)
            {
                var span = u.SubscriptionExpiresAt.Value - now;
                remainingText = $"{span.Days}d {span.Hours}h left";
            }
        }
        else if (u.Status == nameof(UserSubscriptionStatus.Trial) && u.TrialExpiresAt.HasValue)
        {
            expiresAtText = u.TrialExpiresAt.Value.ToString("yyyy-MM-dd HH:mm UTC");
            if (u.TrialExpiresAt.Value > now)
            {
                var span = u.TrialExpiresAt.Value - now;
                remainingText = $"{span.Hours}h {span.Minutes}m left";
            }
        }

        return new
        {
            id = u.Id,
            username = u.Username,
            email = u.Email,
            role = u.Role,
            status = u.Status,
            lastLoginHwid = u.LastLoginHwid,
            remainingText,
            expiresAtText,
            createdAtText = u.CreatedAt.ToString("yyyy-MM-dd HH:mm")
        };
    }).ToList();

    return Results.Ok(new
    {
        totalUsers = users.Count,
        activeSubscribers,
        activeTrials,
        expiredCount,
        registeredDevices = devicesCount,
        users = userList
    });
});

app.MapPost("/api/admin/extend-subscription", async ([FromBody] SubscriptionUpdateRequest req, ServerDbContext db, TelegramNotifier telegram) =>
{
    var user = await db.Users.FindAsync(req.UserId);
    if (user == null) return Results.NotFound();

    var baseDate = (user.SubscriptionExpiresAt.HasValue && user.SubscriptionExpiresAt.Value > DateTime.UtcNow)
        ? user.SubscriptionExpiresAt.Value
        : DateTime.UtcNow;

    user.SubscriptionExpiresAt = baseDate.AddMonths(req.AddMonths);
    user.Status = nameof(UserSubscriptionStatus.Subscribed);
    await db.SaveChangesAsync();

    await telegram.SendSubscriptionExtendedAlertAsync(user.Username, req.AddMonths, user.SubscriptionExpiresAt.Value);
    return Results.Ok(new { success = true, newExpiresAt = user.SubscriptionExpiresAt });
});

app.MapPost("/api/admin/extend-trial", async ([FromBody] JsonElement body, ServerDbContext db) =>
{
    int userId = body.GetProperty("userId").GetInt32();
    int addDays = body.GetProperty("addDays").GetInt32();

    var user = await db.Users.FindAsync(userId);
    if (user == null) return Results.NotFound();

    var baseDate = (user.TrialExpiresAt.HasValue && user.TrialExpiresAt.Value > DateTime.UtcNow)
        ? user.TrialExpiresAt.Value
        : DateTime.UtcNow;

    user.TrialExpiresAt = baseDate.AddDays(addDays);
    user.Status = nameof(UserSubscriptionStatus.Trial);

    if (!string.IsNullOrWhiteSpace(user.LastLoginHwid))
    {
        var dev = await db.Devices.FirstOrDefaultAsync(d => d.HardwareId == user.LastLoginHwid);
        if (dev != null)
        {
            dev.TrialExpiresAt = user.TrialExpiresAt.Value;
            dev.IsTrialConsumed = false;
        }
    }

    await db.SaveChangesAsync();
    return Results.Ok(new { success = true, newTrialExpiresAt = user.TrialExpiresAt });
});

app.MapPost("/api/admin/block-device", async ([FromBody] BlockDeviceRequest req, ServerDbContext db) =>
{
    var device = await db.Devices.FirstOrDefaultAsync(d => d.HardwareId == req.HardwareId);
    if (device == null)
    {
        device = new DeviceRegistration
        {
            HardwareId = req.HardwareId,
            IsBlocked = req.Block,
            BlockReason = req.Reason,
            FirstSeenAt = DateTime.UtcNow
        };
        db.Devices.Add(device);
    }
    else
    {
        device.IsBlocked = req.Block;
        device.BlockReason = req.Reason;
    }

    await db.SaveChangesAsync();
    return Results.Ok(new { success = true, isBlocked = device.IsBlocked });
});

app.Run();
