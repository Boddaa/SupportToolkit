using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NetworkDiscoveryTool.Server.Data;
using NetworkDiscoveryTool.Server.Models;
using NetworkDiscoveryTool.Server.Services;

var builder = WebApplication.CreateBuilder(args);

// Configure Database: Supabase PostgreSQL (Production) or local SQLite fallback
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
builder.Services.AddHostedService<TelegramPollingService>();
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

// Initialize Database Schema & Seed Default Admin
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
                "Notes" VARCHAR(500),
                "MaxDevices" INTEGER NOT NULL DEFAULT 3,
                "ParentUserId" INTEGER NULL
            );

            DO $$
            BEGIN
                IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name='Users' AND column_name='MaxDevices') THEN
                    ALTER TABLE "Users" ADD COLUMN "MaxDevices" INTEGER NOT NULL DEFAULT 3;
                END IF;
                IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name='Users' AND column_name='ParentUserId') THEN
                    ALTER TABLE "Users" ADD COLUMN "ParentUserId" INTEGER NULL;
                END IF;
            END $$;

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

            CREATE TABLE IF NOT EXISTS "UserDevices" (
                "Id" SERIAL PRIMARY KEY,
                "UserId" INTEGER NOT NULL,
                "HardwareId" VARCHAR(100) NOT NULL,
                "MachineName" VARCHAR(100),
                "BoundAt" TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
                "LastUsedAt" TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
                "IsActive" BOOLEAN NOT NULL DEFAULT TRUE
            );

            CREATE INDEX IF NOT EXISTS "IX_UserDevices_UserId" ON "UserDevices" ("UserId");
            CREATE INDEX IF NOT EXISTS "IX_UserDevices_HardwareId" ON "UserDevices" ("HardwareId");

            CREATE TABLE IF NOT EXISTS "AuditLogs" (
                "Id" SERIAL PRIMARY KEY,
                "Action" VARCHAR(100) NOT NULL,
                "Details" TEXT,
                "Timestamp" TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP
            );

            CREATE TABLE IF NOT EXISTS "AppReleases" (
                "Id" SERIAL PRIMARY KEY,
                "Version" VARCHAR(50) NOT NULL,
                "ReleaseNotes" TEXT,
                "DownloadUrl" VARCHAR(500) NOT NULL,
                "IsMandatory" BOOLEAN NOT NULL DEFAULT FALSE,
                "FileSize" BIGINT NOT NULL DEFAULT 0,
                "FileHash" VARCHAR(100),
                "PublishedAt" TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
                "IsActive" BOOLEAN NOT NULL DEFAULT TRUE
            );
        """);
    }
    else
    {
        await db.Database.EnsureCreatedAsync();
        try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"Users\" ADD COLUMN \"MaxDevices\" INTEGER NOT NULL DEFAULT 3"); } catch { }
        try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"Users\" ADD COLUMN \"ParentUserId\" INTEGER NULL"); } catch { }
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
            CreatedAt = DateTime.UtcNow,
            MaxDevices = 10
        };
        db.Users.Add(defaultAdmin);
        await db.SaveChangesAsync();
    }

    if (!await db.AppReleases.AnyAsync())
    {
        db.AppReleases.Add(new AppRelease
        {
            Version = "2.5.0",
            ReleaseNotes = "Initial official release with Motherboard hardware binding & cloud identity.",
            DownloadUrl = "/downloads/SupportToolKit.exe",
            IsMandatory = false,
            PublishedAt = DateTime.UtcNow,
            IsActive = true
        });
        await db.SaveChangesAsync();
    }

    // Ensure downloads directory exists
    var downloadsDir = Path.Combine(app.Environment.WebRootPath ?? Path.Combine(AppContext.BaseDirectory, "wwwroot"), "downloads");
    Directory.CreateDirectory(downloadsDir);
}

// =========================================================================
// 1. DEVICE PRE-CHECK (Hardware & Multi-Device Validation)
// =========================================================================
app.MapPost("/api/device/check", async ([FromBody] DeviceCheckRequest req, ServerDbContext db) =>
{
    if (string.IsNullOrWhiteSpace(req.HardwareId))
        return Results.BadRequest(new { error = "HardwareId required" });

    var hwid = req.HardwareId.Trim();
    var now = DateTime.UtcNow;

    // Check if this device is bound to an active paid subscriber
    var boundDevice = await db.UserDevices.FirstOrDefaultAsync(ud => ud.HardwareId == hwid && ud.IsActive);
    if (boundDevice != null)
    {
        var subscriber = await db.Users.FindAsync(boundDevice.UserId);
        if (subscriber != null)
        {
            var effectiveExpiry = subscriber.ParentUserId.HasValue
                ? (await db.Users.FindAsync(subscriber.ParentUserId.Value))?.SubscriptionExpiresAt
                : subscriber.SubscriptionExpiresAt;

            if (effectiveExpiry.HasValue && effectiveExpiry.Value > now)
            {
                return Results.Ok(new
                {
                    IsKnown = true,
                    CanStartTrial = false,
                    IsBlocked = false,
                    Message = $"Device linked to active subscription (@{subscriber.Username})",
                    SubscribedUser = subscriber.Username,
                    SubscriptionExpiresAt = effectiveExpiry
                });
            }
        }
    }

    var device = await db.Devices.FirstOrDefaultAsync(d => d.HardwareId == hwid);
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
            Message = device.BlockReason ?? "This computer (Motherboard HWID) is administratively blocked."
        });
    }

    bool isTrialExpired = device.IsTrialConsumed || device.TrialExpiresAt <= now;
    return Results.Ok(new
    {
        IsKnown = true,
        CanStartTrial = !isTrialExpired,
        IsBlocked = isTrialExpired,
        TrialExpiresAt = device.TrialExpiresAt,
        IsTrialConsumed = device.IsTrialConsumed,
        Message = isTrialExpired
            ? "This device has already consumed its 1-day free trial. Subscription required to activate this device."
            : "Trial active on this device."
    });
});

// =========================================================================
// 2. USER REGISTRATION (STRICT ANTI-ABUSE: Exactly 1 Account Per Physical PC)
// =========================================================================
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

    // ANTI-ABUSE 1: Blocked device check
    if (existingDevice != null && existingDevice.IsBlocked)
    {
        return Results.Json(new AuthResponse(false, existingDevice.BlockReason ?? "This computer (Motherboard HWID) has been blocked. Contact administrator."), statusCode: 403);
    }

    // ANTI-ABUSE 2: Has this physical machine already registered an account?
    // STRICT RULE: 1 Device = Exactly 1 Trial Account!
    if (existingDevice != null && existingDevice.AssociatedUserId.HasValue)
    {
        var originalUser = await db.Users.FindAsync(existingDevice.AssociatedUserId.Value);
        if (originalUser != null)
        {
            return Results.Json(new AuthResponse(
                Success: false,
                Message: $"This computer (Motherboard HWID) already registered account '@{originalUser.Username}'. Only 1 trial account per device is permitted. Please log in or subscribe."
            ), statusCode: 409);
        }
        else
        {
            // Previously associated user no longer exists in DB! Reset association.
            existingDevice.AssociatedUserId = null;
            existingDevice.IsTrialConsumed = false;
        }
    }

    // ANTI-ABUSE 3: Has this machine consumed trial previously?
    if (existingDevice != null && existingDevice.AssociatedUserId.HasValue && (existingDevice.IsTrialConsumed || existingDevice.TrialExpiresAt <= DateTime.UtcNow))
    {
        return Results.Json(new AuthResponse(
            Success: false,
            Message: "This device has already consumed its 1-day free trial. Registering new accounts on this PC is blocked. Subscribe to activate.",
            Status: nameof(UserSubscriptionStatus.Expired)
        ), statusCode: 403);
    }

    // New valid machine -> Grant 30-Day Free Trial / Subscription
    var trialEnd = DateTime.UtcNow.AddDays(30);
    var user = new ServerUser
    {
        Username = username,
        Email = req.Email?.Trim() ?? string.Empty,
        PasswordHash = PasswordSecurity.HashPassword(req.Password),
        Role = "User",
        Status = nameof(UserSubscriptionStatus.Trial),
        TrialStartedAt = DateTime.UtcNow,
        TrialExpiresAt = trialEnd,
        CreatedAt = DateTime.UtcNow,
        LastLoginHwid = hwid,
        LastLoginIp = http.Connection.RemoteIpAddress?.ToString(),
        MaxDevices = 3
    };

    if (existingDevice == null)
    {
        existingDevice = new DeviceRegistration
        {
            HardwareId = hwid,
            MachineName = req.MachineName?.Trim() ?? "Workstation",
            FirstSeenAt = DateTime.UtcNow,
            TrialExpiresAt = trialEnd,
            IsTrialConsumed = false,
            LastSeenAt = DateTime.UtcNow
        };
        db.Devices.Add(existingDevice);
    }
    else
    {
        existingDevice.TrialExpiresAt = trialEnd;
        existingDevice.IsTrialConsumed = false;
        existingDevice.LastSeenAt = DateTime.UtcNow;
    }

    db.Users.Add(user);
    await db.SaveChangesAsync();

    // Link device permanently to this user
    existingDevice.AssociatedUserId = user.Id;

    // Register initial device binding
    db.UserDevices.Add(new UserDevice
    {
        UserId = user.Id,
        HardwareId = hwid,
        MachineName = req.MachineName?.Trim() ?? "Primary Workstation",
        BoundAt = DateTime.UtcNow,
        LastUsedAt = DateTime.UtcNow,
        IsActive = true
    });

    await db.SaveChangesAsync();

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

    var remainingHours = (trialEnd - DateTime.UtcNow).TotalHours;
    return Results.Ok(new AuthResponse(
        Success: true,
        Message: "Account created! 30-Day Subscription is active on this device.",
        Status: nameof(UserSubscriptionStatus.Trial),
        Role: user.Role,
        ExpiresAt: trialEnd,
        RemainingHours: Math.Round(remainingHours, 1),
        UserId: user.Id
    ));
});

// =========================================================================
// 2B. OFFLINE USER SYNCHRONIZATION (When internet becomes available)
// =========================================================================
app.MapPost("/api/auth/sync-offline-user", async (
    [FromBody] RegisterRequest req,
    ServerDbContext db,
    TelegramNotifier telegram,
    HttpContext http) =>
{
    if (string.IsNullOrWhiteSpace(req.Username) || string.IsNullOrWhiteSpace(req.Password))
        return Results.BadRequest(new AuthResponse(false, "Username and password are required."));

    var username = req.Username.Trim();
    var hwid = req.HardwareId?.Trim() ?? string.Empty;

    // Check if user already exists
    var existingUser = await db.Users.FirstOrDefaultAsync(u => u.Username.ToLower() == username.ToLower());
    if (existingUser != null)
    {
        return Results.Ok(new AuthResponse(
            Success: true,
            Message: "User already synchronized on server.",
            Status: existingUser.Status,
            Role: existingUser.Role,
            ExpiresAt: existingUser.TrialExpiresAt,
            UserId: existingUser.Id
        ));
    }

    var trialEnd = DateTime.UtcNow.AddDays(30);
    var user = new ServerUser
    {
        Username = username,
        Email = req.Email?.Trim() ?? string.Empty,
        PasswordHash = PasswordSecurity.HashPassword(req.Password),
        Role = "User",
        Status = nameof(UserSubscriptionStatus.Trial),
        TrialStartedAt = DateTime.UtcNow,
        TrialExpiresAt = trialEnd,
        CreatedAt = DateTime.UtcNow,
        LastLoginHwid = hwid,
        LastLoginIp = http.Connection.RemoteIpAddress?.ToString(),
        MaxDevices = 3
    };

    var existingDevice = await db.Devices.FirstOrDefaultAsync(d => d.HardwareId == hwid);
    if (existingDevice == null && !string.IsNullOrWhiteSpace(hwid))
    {
        existingDevice = new DeviceRegistration
        {
            HardwareId = hwid,
            MachineName = req.MachineName?.Trim() ?? "Workstation",
            FirstSeenAt = DateTime.UtcNow,
            TrialExpiresAt = trialEnd,
            IsTrialConsumed = false,
            LastSeenAt = DateTime.UtcNow
        };
        db.Devices.Add(existingDevice);
    }
    else if (existingDevice != null)
    {
        existingDevice.TrialExpiresAt = trialEnd;
        existingDevice.LastSeenAt = DateTime.UtcNow;
    }

    db.Users.Add(user);
    await db.SaveChangesAsync();

    if (existingDevice != null)
    {
        existingDevice.AssociatedUserId = user.Id;
        db.UserDevices.Add(new UserDevice
        {
            UserId = user.Id,
            HardwareId = hwid,
            MachineName = req.MachineName?.Trim() ?? "Primary Workstation",
            BoundAt = DateTime.UtcNow,
            LastUsedAt = DateTime.UtcNow,
            IsActive = true
        });
        await db.SaveChangesAsync();
    }

    _ = Task.Run(async () =>
    {
        await telegram.SendTrialRegistrationAlertAsync(
            user.Id,
            $"{user.Username} (Offline Synced)",
            string.IsNullOrWhiteSpace(user.Email) ? "None" : user.Email,
            hwid,
            req.MachineName ?? "PC");
    });

    var remainingHours = (trialEnd - DateTime.UtcNow).TotalHours;
    return Results.Ok(new AuthResponse(
        Success: true,
        Message: "Offline account synchronized with 30-Day Subscription.",
        Status: nameof(UserSubscriptionStatus.Trial),
        Role: user.Role,
        ExpiresAt: trialEnd,
        RemainingHours: Math.Round(remainingHours, 1),
        UserId: user.Id
    ));
});

// =========================================================================
// 3. USER LOGIN (User-Centric Licensing with Multi-Device / Family up to 3)
// =========================================================================
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
    var machineName = req.MachineName?.Trim() ?? "Workstation";

    // Hardware Block Check
    if (!string.IsNullOrWhiteSpace(hwid))
    {
        var device = await db.Devices.FirstOrDefaultAsync(d => d.HardwareId == hwid);
        if (device != null && device.IsBlocked && device.BlockReason != null && device.BlockReason.Contains("Admin blocked"))
        {
            return Results.Json(new AuthResponse(false, device.BlockReason), statusCode: 403);
        }
    }

    var now = DateTime.UtcNow;

    // Check if user is a Family Member (inheriting parent's subscription)
    ServerUser effectiveSubscriber = user;
    if (user.ParentUserId.HasValue)
    {
        var parentUser = await db.Users.FindAsync(user.ParentUserId.Value);
        if (parentUser != null)
        {
            effectiveSubscriber = parentUser;
        }
    }

    // -------------------------------------------------------------------------
    // CASE A: ACTIVE PAID SUBSCRIPTION (Allows up to 3 Devices / Family Use)
    // -------------------------------------------------------------------------
    if (effectiveSubscriber.Status == nameof(UserSubscriptionStatus.Subscribed))
    {
        if (effectiveSubscriber.SubscriptionExpiresAt.HasValue && effectiveSubscriber.SubscriptionExpiresAt.Value > now)
        {
            int maxAllowed = effectiveSubscriber.MaxDevices > 0 ? effectiveSubscriber.MaxDevices : 3;

            if (!string.IsNullOrWhiteSpace(hwid))
            {
                // Check if device is already registered for this user or family
                var existingBinding = await db.UserDevices.FirstOrDefaultAsync(ud =>
                    (ud.UserId == user.Id || ud.UserId == effectiveSubscriber.Id) && ud.HardwareId == hwid && ud.IsActive);

                if (existingBinding != null)
                {
                    existingBinding.LastUsedAt = now;
                    existingBinding.MachineName = machineName;
                }
                else
                {
                    // New device being added under this user's subscription!
                    var activeCount = await db.UserDevices.CountAsync(ud =>
                        (ud.UserId == user.Id || ud.UserId == effectiveSubscriber.Id) && ud.IsActive);

                    if (activeCount >= maxAllowed)
                    {
                        return Results.Json(new AuthResponse(
                            Success: false,
                            Message: $"Device limit reached ({activeCount}/{maxAllowed} devices in use). You can unlink an old device in the dashboard.",
                            Status: "DeviceLimitReached"
                        ), statusCode: 403);
                    }

                    // Register new device slot
                    db.UserDevices.Add(new UserDevice
                    {
                        UserId = user.Id,
                        HardwareId = hwid,
                        MachineName = machineName,
                        BoundAt = now,
                        LastUsedAt = now,
                        IsActive = true
                    });
                }

                // Unblock device in Devices table since an active subscriber owns it now!
                var devReg = await db.Devices.FirstOrDefaultAsync(d => d.HardwareId == hwid);
                if (devReg != null)
                {
                    devReg.IsBlocked = false;
                    devReg.BlockReason = null;
                    devReg.LastSeenAt = now;
                }
                else
                {
                    db.Devices.Add(new DeviceRegistration
                    {
                        HardwareId = hwid,
                        MachineName = machineName,
                        AssociatedUserId = user.Id,
                        FirstSeenAt = now,
                        LastSeenAt = now,
                        IsBlocked = false,
                        IsTrialConsumed = true
                    });
                }
            }

            user.LastLoginAt = now;
            user.LastLoginHwid = hwid;
            user.LastLoginIp = http.Connection.RemoteIpAddress?.ToString();
            await db.SaveChangesAsync();

            var hours = (effectiveSubscriber.SubscriptionExpiresAt.Value - now).TotalHours;
            var activeDeviceCount = await db.UserDevices.CountAsync(ud =>
                (ud.UserId == user.Id || ud.UserId == effectiveSubscriber.Id) && ud.IsActive);

            return Results.Ok(new AuthResponse(
                Success: true,
                Message: $"Login successful (Active Subscription - Device {activeDeviceCount}/{maxAllowed}).",
                Status: nameof(UserSubscriptionStatus.Subscribed),
                Role: user.Role,
                ExpiresAt: effectiveSubscriber.SubscriptionExpiresAt,
                RemainingHours: Math.Round(hours, 1),
                UserId: user.Id
            ));
        }

        // Subscription Expired
        user.Status = nameof(UserSubscriptionStatus.Expired);
        if (effectiveSubscriber != user) effectiveSubscriber.Status = nameof(UserSubscriptionStatus.Expired);
        await db.SaveChangesAsync();

        return Results.Json(new AuthResponse(
            Success: false,
            Message: $"Subscription expired on {effectiveSubscriber.SubscriptionExpiresAt:yyyy-MM-dd}. Please renew to continue.",
            Status: nameof(UserSubscriptionStatus.Expired)
        ), statusCode: 403);
    }

    // -------------------------------------------------------------------------
    // CASE B: FREE TRIAL (24 Hours - Restricted to 1 Registered Device)
    // -------------------------------------------------------------------------
    if (user.Status == nameof(UserSubscriptionStatus.Trial))
    {
        if (user.TrialExpiresAt.HasValue && user.TrialExpiresAt.Value > now)
        {
            // Trial can only be used on the original device it was registered on
            if (!string.IsNullOrWhiteSpace(hwid) && !string.IsNullOrWhiteSpace(user.LastLoginHwid) &&
                !string.Equals(hwid, user.LastLoginHwid, StringComparison.OrdinalIgnoreCase))
            {
                return Results.Json(new AuthResponse(
                    Success: false,
                    Message: "Free Trial is restricted to the single device it was activated on. Subscribe to use across multiple devices.",
                    Status: "DeviceMismatch"
                ), statusCode: 403);
            }

            user.LastLoginAt = now;
            user.LastLoginHwid = hwid;
            user.LastLoginIp = http.Connection.RemoteIpAddress?.ToString();
            await db.SaveChangesAsync();

            var hours = (user.TrialExpiresAt.Value - now).TotalHours;
            return Results.Ok(new AuthResponse(
                Success: true,
                Message: "Login successful (Free Trial - 1 Device).",
                Status: user.Status,
                Role: user.Role,
                ExpiresAt: user.TrialExpiresAt,
                RemainingHours: Math.Round(hours, 1),
                UserId: user.Id
            ));
        }

        // Trial Expired -> Lock device and user!
        user.Status = nameof(UserSubscriptionStatus.Expired);
        if (!string.IsNullOrWhiteSpace(hwid))
        {
            var dev = await db.Devices.FirstOrDefaultAsync(d => d.HardwareId == hwid);
            if (dev != null)
            {
                dev.IsTrialConsumed = true;
                dev.IsBlocked = true;
                dev.BlockReason = "1-day free trial expired. Subscribe to unlock device.";
            }
        }
        await db.SaveChangesAsync();

        return Results.Json(new AuthResponse(
            Success: false,
            Message: "Your 1-day free trial has expired. This device is locked. Please subscribe to unlock full access.",
            Status: nameof(UserSubscriptionStatus.Expired)
        ), statusCode: 403);
    }

    // -------------------------------------------------------------------------
    // CASE C: EXPIRED OR SUSPENDED
    // -------------------------------------------------------------------------
    return Results.Json(new AuthResponse(
        Success: false,
        Message: "Account access expired or suspended. Contact administrator on Telegram to subscribe.",
        Status: user.Status
    ), statusCode: 403);
});

// =========================================================================
// 3.5 CHECK APPROVAL STATUS (Immediate Cloud Polling for Waiting Clients)
// =========================================================================
app.MapGet("/api/auth/check-approval", async ([FromQuery] string? username, [FromQuery] string? hardwareId, ServerDbContext db) =>
{
    if (string.IsNullOrWhiteSpace(username))
        return Results.BadRequest(new { approved = false, status = "Pending", message = "Username required" });

    var cleanUser = username.Trim().ToLower();

    // 1. Check in-memory approval cache (fastest, catches instant Telegram approvals)
    if (TelegramPollingService.ApprovedUsersCache.TryGetValue(cleanUser, out var isAppr) && isAppr)
    {
        return Results.Ok(new { approved = true, status = "Approved", message = "Approved by Administrator via Telegram." });
    }
    if (TelegramPollingService.RejectedUsersCache.TryGetValue(cleanUser, out var isRejc) && isRejc)
    {
        return Results.Ok(new { approved = false, rejected = true, status = "Rejected", message = "Rejected by Administrator." });
    }

    // 2. Check Database
    var user = await db.Users.FirstOrDefaultAsync(u => u.Username.ToLower() == cleanUser);
    if (user != null)
    {
        if (user.Status == nameof(UserSubscriptionStatus.Suspended))
        {
            return Results.Ok(new { approved = false, rejected = true, status = "Suspended", message = "User is suspended." });
        }

        if (user.IsApproved ||
            user.Status == nameof(UserSubscriptionStatus.Subscribed) ||
            user.Status == nameof(UserSubscriptionStatus.Trial))
        {
            return Results.Ok(new { approved = true, status = user.Status, message = "User is approved and active." });
        }
    }

    return Results.Ok(new { approved = false, status = "Pending", message = "Waiting for administrator approval." });
});

// =========================================================================
// 4. ADMIN DASHBOARD API (Multi-Device & Family Management)
// =========================================================================
app.MapGet("/api/admin/dashboard-stats", async (ServerDbContext db) =>
{
    var now = DateTime.UtcNow;
    var users = await db.Users.OrderByDescending(u => u.CreatedAt).ToListAsync();
    var allUserDevices = await db.UserDevices.Where(ud => ud.IsActive).ToListAsync();
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

        var effectiveExpiry = u.ParentUserId.HasValue
            ? users.FirstOrDefault(p => p.Id == u.ParentUserId.Value)?.SubscriptionExpiresAt
            : u.SubscriptionExpiresAt;

        var effectiveStatus = u.ParentUserId.HasValue
            ? users.FirstOrDefault(p => p.Id == u.ParentUserId.Value)?.Status ?? u.Status
            : u.Status;

        if (effectiveStatus == nameof(UserSubscriptionStatus.Subscribed) && effectiveExpiry.HasValue)
        {
            expiresAtText = effectiveExpiry.Value.ToString("yyyy-MM-dd HH:mm UTC");
            if (effectiveExpiry.Value > now)
            {
                var span = effectiveExpiry.Value - now;
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

        var userDevices = allUserDevices
            .Where(ud => ud.UserId == u.Id || (u.ParentUserId.HasValue && ud.UserId == u.ParentUserId.Value))
            .Select(ud => new
            {
                id = ud.Id,
                hardwareId = ud.HardwareId,
                machineName = ud.MachineName,
                boundAt = ud.BoundAt.ToString("yyyy-MM-dd HH:mm"),
                lastUsed = ud.LastUsedAt.ToString("yyyy-MM-dd HH:mm")
            }).ToList();

        var familyMembers = users
            .Where(f => f.ParentUserId == u.Id)
            .Select(f => new
            {
                id = f.Id,
                username = f.Username,
                email = f.Email,
                status = f.Status,
                createdAtText = f.CreatedAt.ToString("yyyy-MM-dd HH:mm")
            }).ToList();

        var parentName = u.ParentUserId.HasValue
            ? users.FirstOrDefault(p => p.Id == u.ParentUserId.Value)?.Username
            : null;

        return new
        {
            id = u.Id,
            username = u.Username,
            email = u.Email,
            role = u.Role,
            status = effectiveStatus,
            lastLoginHwid = u.LastLoginHwid,
            remainingText,
            expiresAtText,
            createdAtText = u.CreatedAt.ToString("yyyy-MM-dd HH:mm"),
            devicesCount = userDevices.Count,
            maxDevices = u.MaxDevices > 0 ? u.MaxDevices : 3,
            devices = userDevices,
            isFamily = u.ParentUserId.HasValue,
            parentUsername = parentName,
            familyMembers = familyMembers
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

    // Unblock any devices associated with this user
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
            dev.IsBlocked = false;
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
            BlockReason = req.Reason ?? (req.Block ? "Admin blocked" : null),
            FirstSeenAt = DateTime.UtcNow
        };
        db.Devices.Add(device);
    }
    else
    {
        device.IsBlocked = req.Block;
        device.BlockReason = req.Reason ?? (req.Block ? "Admin blocked" : null);
    }

    await db.SaveChangesAsync();
    return Results.Ok(new { success = true, isBlocked = device.IsBlocked });
});

// Unlink a device to free up a slot out of 3
app.MapPost("/api/admin/unlink-device", async ([FromBody] UnlinkDeviceRequest req, ServerDbContext db) =>
{
    var binding = await db.UserDevices.FirstOrDefaultAsync(ud => ud.UserId == req.UserId && ud.HardwareId == req.HardwareId && ud.IsActive);
    if (binding == null) return Results.NotFound(new { success = false, message = "Device binding not found." });

    binding.IsActive = false;
    await db.SaveChangesAsync();

    return Results.Ok(new { success = true, message = $"Device {req.HardwareId} unlinked successfully. Device slot freed up." });
});

// Create a Family Member sub-user under a primary subscription
app.MapPost("/api/admin/create-family-member", async ([FromBody] CreateFamilyMemberRequest req, ServerDbContext db) =>
{
    var parent = await db.Users.FindAsync(req.ParentUserId);
    if (parent == null) return Results.NotFound(new { success = false, message = "Parent user not found." });

    var cleanUsername = req.Username.Trim();
    if (await db.Users.AnyAsync(u => u.Username.ToLower() == cleanUsername.ToLower()))
        return Results.Conflict(new { success = false, message = "Username already exists." });

    // Check family members count (max 2 sub-users -> total 3 accounts)
    var currentFamily = await db.Users.CountAsync(u => u.ParentUserId == parent.Id);
    if (currentFamily >= 2)
    {
        return Results.BadRequest(new { success = false, message = "Maximum 2 family sub-accounts allowed per primary subscription." });
    }

    var subUser = new ServerUser
    {
        Username = cleanUsername,
        Email = req.Email?.Trim() ?? string.Empty,
        PasswordHash = PasswordSecurity.HashPassword(req.Password),
        Role = "User",
        Status = parent.Status,
        SubscriptionExpiresAt = parent.SubscriptionExpiresAt,
        ParentUserId = parent.Id,
        MaxDevices = 3,
        CreatedAt = DateTime.UtcNow
    };

    db.Users.Add(subUser);
    await db.SaveChangesAsync();

    return Results.Ok(new { success = true, message = $"Family account '@{cleanUsername}' created under parent '@{parent.Username}'." });
});

// Remove a family sub-account
app.MapPost("/api/admin/remove-family-member", async ([FromBody] JsonElement body, ServerDbContext db) =>
{
    int subUserId = body.GetProperty("subUserId").GetInt32();
    var subUser = await db.Users.FindAsync(subUserId);
    if (subUser == null || !subUser.ParentUserId.HasValue)
        return Results.NotFound(new { success = false, message = "Family sub-account not found." });

    var userDevices = await db.UserDevices.Where(ud => ud.UserId == subUserId).ToListAsync();
    db.UserDevices.RemoveRange(userDevices);

    db.Users.Remove(subUser);
    await db.SaveChangesAsync();

    return Results.Ok(new { success = true, message = $"Family account '@{subUser.Username}' removed successfully." });
});

// Admin: Delete any user account (cleans up family members, device bindings, and frees up HWIDs)
app.MapPost("/api/admin/delete-user", async ([FromBody] JsonElement body, ServerDbContext db) =>
{
    if (!body.TryGetProperty("userId", out var uidProp) || !uidProp.TryGetInt32(out var userId))
        return Results.BadRequest(new { success = false, message = "Valid userId required." });

    var user = await db.Users.FindAsync(userId);
    if (user == null)
        return Results.NotFound(new { success = false, message = "User not found." });

    if (string.Equals(user.Role, "Admin", StringComparison.OrdinalIgnoreCase) && user.Id == 1)
        return Results.BadRequest(new { success = false, message = "Cannot delete primary administrator account." });

    // 1. Remove child family members if any
    var familyMembers = await db.Users.Where(u => u.ParentUserId == user.Id).ToListAsync();
    foreach (var fm in familyMembers)
    {
        var fmDevices = await db.UserDevices.Where(ud => ud.UserId == fm.Id).ToListAsync();
        db.UserDevices.RemoveRange(fmDevices);
        db.Users.Remove(fm);
    }

    // 2. Remove user devices
    var userDevices = await db.UserDevices.Where(ud => ud.UserId == user.Id).ToListAsync();
    db.UserDevices.RemoveRange(userDevices);

    // 3. Clear association in registered devices
    var boundDevices = await db.Devices.Where(d => d.AssociatedUserId == user.Id).ToListAsync();
    foreach (var dev in boundDevices)
    {
        dev.AssociatedUserId = null;
        dev.IsTrialConsumed = false;
    }

    // 4. Remove user
    db.Users.Remove(user);
    await db.SaveChangesAsync();

    return Results.Ok(new { success = true, message = $"Account '@{user.Username}' and associated devices deleted successfully." });
});

// =========================================================================
// 4.5 CLIENT-FACING FAMILY / TEAM MANAGEMENT (Only for Subscribed Users)
// =========================================================================

app.MapGet("/api/user/family-members", async ([FromQuery] string? parentUsername, [FromQuery] int? parentUserId, ServerDbContext db) =>
{
    if (string.IsNullOrWhiteSpace(parentUsername) && !parentUserId.HasValue)
        return Results.BadRequest(new { success = false, message = "Parent identifier required." });

    ServerUser? parent = null;
    if (!string.IsNullOrWhiteSpace(parentUsername))
    {
        var clean = parentUsername.Trim().ToLower();
        parent = await db.Users.FirstOrDefaultAsync(u => u.Username.ToLower() == clean);
    }
    if (parent == null && parentUserId.HasValue && parentUserId.Value > 0)
    {
        parent = await db.Users.FindAsync(parentUserId.Value);
    }

    if (parent == null)
        return Results.NotFound(new { success = false, message = "User not found." });

    bool isSubscribed = (parent.Status == nameof(UserSubscriptionStatus.Subscribed) &&
                         parent.SubscriptionExpiresAt.HasValue &&
                         parent.SubscriptionExpiresAt.Value > DateTime.UtcNow) ||
                        string.Equals(parent.Role, "Admin", StringComparison.OrdinalIgnoreCase);

    var members = await db.Users
        .Where(u => u.ParentUserId == parent.Id)
        .OrderByDescending(u => u.CreatedAt)
        .Select(u => new
        {
            id = u.Id,
            username = u.Username,
            email = u.Email,
            createdAt = u.CreatedAt.ToString("yyyy-MM-dd HH:mm"),
            status = u.Status
        })
        .ToListAsync();

    return Results.Ok(new
    {
        success = true,
        isSubscribed = isSubscribed,
        maxSlots = 2,
        usedSlots = members.Count,
        availableSlots = Math.Max(0, 2 - members.Count),
        members = members
    });
});

app.MapPost("/api/user/create-family-member", async ([FromBody] JsonElement body, ServerDbContext db) =>
{
    string parentUsername = body.TryGetProperty("parentUsername", out var pu) && pu.ValueKind == JsonValueKind.String ? pu.GetString() ?? "" : "";
    int? parentUserId = body.TryGetProperty("parentUserId", out var pid) && pid.ValueKind == JsonValueKind.Number && pid.TryGetInt32(out var idVal) ? idVal : null;
    string newUsername = body.TryGetProperty("username", out var u) && u.ValueKind == JsonValueKind.String ? u.GetString() ?? "" : "";
    string newPassword = body.TryGetProperty("password", out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() ?? "" : "";
    string email = body.TryGetProperty("email", out var em) && em.ValueKind == JsonValueKind.String ? em.GetString() ?? "" : "";

    ServerUser? parent = null;
    if (!string.IsNullOrWhiteSpace(parentUsername))
    {
        var clean = parentUsername.Trim().ToLower();
        parent = await db.Users.FirstOrDefaultAsync(user => user.Username.ToLower() == clean);
    }
    if (parent == null && parentUserId.HasValue && parentUserId.Value > 0)
    {
        parent = await db.Users.FindAsync(parentUserId.Value);
    }

    if (parent == null)
        return Results.NotFound(new { success = false, message = "User account not found." });

    // CRITICAL: Only Subscribed users (or Admin) can create sub-accounts!
    bool isSubscribed = (parent.Status == nameof(UserSubscriptionStatus.Subscribed) &&
                         parent.SubscriptionExpiresAt.HasValue &&
                         parent.SubscriptionExpiresAt.Value > DateTime.UtcNow) ||
                        string.Equals(parent.Role, "Admin", StringComparison.OrdinalIgnoreCase);

    if (!isSubscribed)
    {
        return Results.Json(new
        {
            success = false,
            message = "Creating team/family users is reserved exclusively for Subscribed users. Upgrade your subscription to unlock this feature."
        }, statusCode: 403);
    }

    if (parent.ParentUserId.HasValue)
    {
        return Results.BadRequest(new { success = false, message = "Sub-accounts cannot create other sub-accounts." });
    }

    var cleanUsername = newUsername.Trim();
    if (cleanUsername.Length < 3)
        return Results.BadRequest(new { success = false, message = "Username must be at least 3 characters." });

    if (newPassword.Length < 4)
        return Results.BadRequest(new { success = false, message = "Password must be at least 4 characters." });

    if (await db.Users.AnyAsync(u => u.Username.ToLower() == cleanUsername.ToLower()))
        return Results.Conflict(new { success = false, message = $"Username '@{cleanUsername}' is already taken." });

    // Max 2 sub-accounts limit
    var currentCount = await db.Users.CountAsync(u => u.ParentUserId == parent.Id);
    if (currentCount >= 2)
    {
        return Results.BadRequest(new { success = false, message = "You have reached the maximum limit of 2 sub-accounts." });
    }

    var subUser = new ServerUser
    {
        Username = cleanUsername,
        Email = email.Trim(),
        PasswordHash = PasswordSecurity.HashPassword(newPassword),
        Role = "User",
        Status = parent.Status,
        SubscriptionExpiresAt = parent.SubscriptionExpiresAt,
        ParentUserId = parent.Id,
        MaxDevices = 3,
        IsApproved = true,
        CreatedAt = DateTime.UtcNow
    };

    db.Users.Add(subUser);
    await db.SaveChangesAsync();

    return Results.Ok(new
    {
        success = true,
        message = $"User '@{cleanUsername}' created successfully! They can now log in using this subscription.",
        member = new
        {
            id = subUser.Id,
            username = subUser.Username,
            email = subUser.Email,
            createdAt = subUser.CreatedAt.ToString("yyyy-MM-dd HH:mm"),
            status = subUser.Status
        }
    });
});

app.MapPost("/api/user/remove-family-member", async ([FromBody] JsonElement body, ServerDbContext db) =>
{
    string parentUsername = body.TryGetProperty("parentUsername", out var pu) && pu.ValueKind == JsonValueKind.String ? pu.GetString() ?? "" : "";
    int? parentUserId = body.TryGetProperty("parentUserId", out var pid) && pid.ValueKind == JsonValueKind.Number && pid.TryGetInt32(out var idVal) ? idVal : null;
    int subUserId = body.TryGetProperty("subUserId", out var sid) && sid.ValueKind == JsonValueKind.Number && sid.TryGetInt32(out var sVal) ? sVal : 0;

    ServerUser? parent = null;
    if (!string.IsNullOrWhiteSpace(parentUsername))
    {
        var clean = parentUsername.Trim().ToLower();
        parent = await db.Users.FirstOrDefaultAsync(user => user.Username.ToLower() == clean);
    }
    if (parent == null && parentUserId.HasValue && parentUserId.Value > 0)
    {
        parent = await db.Users.FindAsync(parentUserId.Value);
    }

    if (parent == null)
        return Results.NotFound(new { success = false, message = "User not found." });

    var subUser = await db.Users.FindAsync(subUserId);
    if (subUser == null || subUser.ParentUserId != parent.Id)
        return Results.NotFound(new { success = false, message = "Sub-account not found or not owned by you." });

    var userDevices = await db.UserDevices.Where(ud => ud.UserId == subUserId).ToListAsync();
    db.UserDevices.RemoveRange(userDevices);
    db.Users.Remove(subUser);
    await db.SaveChangesAsync();

    return Results.Ok(new { success = true, message = $"Account '@{subUser.Username}' removed successfully. Slot freed up." });
});

// =========================================================================
// 5. AUTO-UPDATE & APP RELEASE ENDPOINTS
// =========================================================================

// Public endpoint: Check for updates
app.MapGet("/api/app/check-update", async ([FromQuery] string? currentVersion, ServerDbContext db, HttpContext http) =>
{
    var latest = await db.AppReleases.Where(r => r.IsActive).OrderByDescending(r => r.PublishedAt).FirstOrDefaultAsync();
    var currStr = (currentVersion ?? "2.5.0").TrimStart('v', 'V');
    var lateStr = (latest?.Version ?? "2.5.0").TrimStart('v', 'V');

    bool hasUpdate = false;
    if (Version.TryParse(currStr, out var vCurr) && Version.TryParse(lateStr, out var vLate))
    {
        hasUpdate = vLate > vCurr;
    }
    else
    {
        hasUpdate = !string.Equals(currStr, lateStr, StringComparison.OrdinalIgnoreCase);
    }

    var downloadUrl = latest?.DownloadUrl ?? "/downloads/SupportToolKit.exe";
    if (!downloadUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
    {
        downloadUrl = $"{http.Request.Scheme}://{http.Request.Host}{downloadUrl}";
    }

    return Results.Ok(new CheckUpdateResponse(
        HasUpdate: hasUpdate,
        LatestVersion: latest?.Version ?? "2.5.0",
        CurrentVersion: currentVersion ?? "2.5.0",
        DownloadUrl: downloadUrl,
        ReleaseNotes: latest?.ReleaseNotes ?? "No update notes available.",
        IsMandatory: latest?.IsMandatory ?? false,
        FileSize: latest?.FileSize ?? 0,
        PublishedAt: latest?.PublishedAt.ToString("yyyy-MM-dd HH:mm UTC") ?? DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm UTC")
    ));
});

// Admin endpoint: Get current release metadata
app.MapGet("/api/admin/latest-release", async (ServerDbContext db) =>
{
    var latest = await db.AppReleases.Where(r => r.IsActive).OrderByDescending(r => r.PublishedAt).FirstOrDefaultAsync();
    return Results.Ok(latest ?? new AppRelease { Version = "2.5.0", ReleaseNotes = "Initial Release" });
});

// Admin endpoint: Publish release via metadata / URL
app.MapPost("/api/admin/publish-release", async ([FromBody] PublishReleaseRequest req, ServerDbContext db) =>
{
    if (string.IsNullOrWhiteSpace(req.Version))
        return Results.BadRequest(new { success = false, message = "Version number is required." });

    var release = new AppRelease
    {
        Version = req.Version.Trim(),
        ReleaseNotes = req.ReleaseNotes?.Trim() ?? string.Empty,
        DownloadUrl = req.DownloadUrl?.Trim() ?? "/downloads/SupportToolKit.exe",
        IsMandatory = req.IsMandatory,
        PublishedAt = DateTime.UtcNow,
        IsActive = true
    };

    db.AppReleases.Add(release);
    await db.SaveChangesAsync();

    return Results.Ok(new { success = true, message = $"Release v{release.Version} published successfully!", release });
});

// Admin endpoint: Upload new SupportToolKit.exe file directly to server
app.MapPost("/api/admin/upload-release-file", async (HttpRequest request, ServerDbContext db, IWebHostEnvironment env) =>
{
    if (!request.HasFormContentType)
        return Results.BadRequest(new { success = false, message = "Multipart form data expected." });

    var form = await request.ReadFormAsync();
    var file = form.Files["file"];
    var version = form["version"].ToString().Trim();
    var notes = form["notes"].ToString().Trim();
    var isMandatory = bool.TryParse(form["isMandatory"], out var mand) && mand;

    if (file == null || file.Length == 0)
        return Results.BadRequest(new { success = false, message = "Executable file is required." });

    if (string.IsNullOrWhiteSpace(version))
        return Results.BadRequest(new { success = false, message = "Version number is required." });

    var downloadsFolder = Path.Combine(env.WebRootPath ?? Path.Combine(AppContext.BaseDirectory, "wwwroot"), "downloads");
    Directory.CreateDirectory(downloadsFolder);

    var targetFilePath = Path.Combine(downloadsFolder, "SupportToolKit.exe");

    // Copy uploaded file
    using (var stream = new FileStream(targetFilePath, FileMode.Create))
    {
        await file.CopyToAsync(stream);
    }

    // Compute SHA256 and size
    string hashHex;
    long fileSize = new FileInfo(targetFilePath).Length;
    using (var sha = System.Security.Cryptography.SHA256.Create())
    using (var fs = File.OpenRead(targetFilePath))
    {
        hashHex = Convert.ToHexString(sha.ComputeHash(fs));
    }

    var release = new AppRelease
    {
        Version = version,
        ReleaseNotes = notes,
        DownloadUrl = "/downloads/SupportToolKit.exe",
        IsMandatory = isMandatory,
        FileSize = fileSize,
        FileHash = hashHex,
        PublishedAt = DateTime.UtcNow,
        IsActive = true
    };

    db.AppReleases.Add(release);
    await db.SaveChangesAsync();

    return Results.Ok(new { 
        success = true, 
        message = $"SupportToolKit.exe (v{version}) uploaded and published! File size: {fileSize / (1024 * 1024)} MB.",
        release 
    });
});

app.Run();
