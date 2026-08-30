using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using NetworkDiscoveryTool.UI.Models;

namespace NetworkDiscoveryTool.UI.Services;

public interface ISqlConnectionService
{
    Task<SqlDiagnosticResult> RunDiagnosticsAsync(
        string server,
        int port,
        string database,
        bool useWindowsAuth,
        string username,
        string password,
        int connectTimeout = 15,
        int commandTimeout = 30,
        bool encrypt = false,
        bool trustCertificate = true,
        string applicationName = "SupportToolKit",
        CancellationToken ct = default);

    Task<List<SqlSavedProfile>> LoadProfilesAsync();
    Task SaveProfileAsync(SqlSavedProfile profile);
    Task DeleteProfileAsync(string id);

    Task<List<SqlTestHistoryItem>> LoadHistoryAsync();
    Task SaveHistoryItemAsync(SqlTestHistoryItem item);
    Task ClearHistoryAsync();

    string EncryptPassword(string plain);
    string DecryptPassword(string? encrypted);
}

public sealed class SqlConnectionService : ISqlConnectionService
{
    private static readonly string ProfilesFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SupportToolKit", "sql_profiles.json");

    private static readonly string HistoryFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SupportToolKit", "sql_history.json");

    public async Task<SqlDiagnosticResult> RunDiagnosticsAsync(
        string server,
        int port,
        string database,
        bool useWindowsAuth,
        string username,
        string password,
        int connectTimeout = 15,
        int commandTimeout = 30,
        bool encrypt = false,
        bool trustCertificate = true,
        string applicationName = "SupportToolKit",
        CancellationToken ct = default)
    {
        return await Task.Run(async () =>
        {
            var result = new SqlDiagnosticResult
            {
                SummaryServer = string.IsNullOrWhiteSpace(server) ? "localhost" : server.Trim(),
                SummaryDatabase = string.IsNullOrWhiteSpace(database) ? "master" : database.Trim(),
                SummaryAuth = useWindowsAuth ? "Windows" : $"SQL Auth ({username})",
                ServerInfo = new SqlServerInfo
                {
                    TcpPort = port > 0 ? port.ToString() : "1433",
                    CurrentDatabase = string.IsNullOrWhiteSpace(database) ? "master" : database.Trim()
                }
            };

            var overallSw = Stopwatch.StartNew();
            string hostOnly = ExtractHostOnly(result.SummaryServer);
            int targetPort = port > 0 ? port : 1433;

            // ========================================================
            // STAGE 1: DNS Resolution
            // ========================================================
            var stageDns = new SqlCheckStage
            {
                Name = "DNS Resolution",
                Description = $"Resolving host '{hostOnly}'"
            };
            result.Checks.Add(stageDns);

            IPAddress? resolvedIp = null;
            var sw = Stopwatch.StartNew();
            try
            {
                stageDns.Status = "Running";
                if (IPAddress.TryParse(hostOnly, out var ip))
                {
                    resolvedIp = ip;
                    sw.Stop();
                    stageDns.LatencyMs = Math.Max(1, sw.ElapsedMilliseconds);
                    stageDns.Status = "Success";
                    stageDns.Details = $"Direct IP ({resolvedIp})";
                }
                else if (hostOnly.Equals("localhost", StringComparison.OrdinalIgnoreCase) || hostOnly == "." || hostOnly == "(local)")
                {
                    resolvedIp = IPAddress.Loopback;
                    sw.Stop();
                    stageDns.LatencyMs = Math.Max(1, sw.ElapsedMilliseconds);
                    stageDns.Status = "Success";
                    stageDns.Details = "Local Loopback (127.0.0.1)";
                }
                else
                {
                    var addresses = await Dns.GetHostAddressesAsync(hostOnly, ct);
                    sw.Stop();
                    resolvedIp = addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork) ?? addresses.FirstOrDefault();
                    stageDns.LatencyMs = Math.Max(1, sw.ElapsedMilliseconds);
                    stageDns.Status = "Success";
                    stageDns.Details = $"Resolved to {resolvedIp}";
                }
            }
            catch (Exception ex)
            {
                sw.Stop();
                stageDns.LatencyMs = sw.ElapsedMilliseconds;
                stageDns.Status = "Failed";
                stageDns.Details = ex.Message;
                result.Success = false;
                result.OverallStatus = "DNS Resolution Failed";
                result.FailureSummary = $"Could not resolve hostname '{hostOnly}'.";
                result.PossibleCauses.AddRange(new[]
                {
                    "Incorrect server name or typo in hostname.",
                    "DNS server is unreachable or host is not registered in local DNS.",
                    "Device is offline or disconnected from the network."
                });
                result.TechnicalDetails = new SqlTechnicalDetails
                {
                    ExceptionType = ex.GetType().Name,
                    Message = ex.Message,
                    ServerTarget = server
                };
                overallSw.Stop();
                result.TotalLatencyMs = overallSw.ElapsedMilliseconds;
                return result;
            }

            // ========================================================
            // STAGE 2: TCP Port Connectivity
            // ========================================================
            var stageTcp = new SqlCheckStage
            {
                Name = $"TCP Port {targetPort}",
                Description = $"Probing TCP socket at {resolvedIp}:{targetPort}"
            };
            result.Checks.Add(stageTcp);

            sw.Restart();
            try
            {
                stageTcp.Status = "Running";
                using var tcp = new TcpClient();
                var connectTask = tcp.ConnectAsync(resolvedIp!, targetPort, ct);
                var delayTask = Task.Delay(Math.Min(connectTimeout * 1000, 4000), ct);

                if (await Task.WhenAny(connectTask.AsTask(), delayTask) == connectTask.AsTask() && tcp.Connected)
                {
                    sw.Stop();
                    stageTcp.LatencyMs = Math.Max(1, sw.ElapsedMilliseconds);
                    stageTcp.Status = "Success";
                    stageTcp.Details = $"Port {targetPort} is Open & Listening";
                }
                else
                {
                    sw.Stop();
                    stageTcp.LatencyMs = sw.ElapsedMilliseconds;
                    stageTcp.Status = "Failed";
                    stageTcp.Details = $"Connection timed out on TCP {targetPort}";
                    
                    // If it's a named instance without static port, warn about SQL Browser
                    if (server.Contains('\\'))
                    {
                        stageTcp.Details += " (Named instance may use dynamic port via UDP 1434)";
                    }
                }
            }
            catch (Exception ex)
            {
                sw.Stop();
                stageTcp.LatencyMs = sw.ElapsedMilliseconds;
                stageTcp.Status = "Failed";
                stageTcp.Details = ex.Message;
            }

            // ========================================================
            // STAGE 3: SQL Handshake & STAGE 4: Authentication & STAGE 5: DB Access
            // ========================================================
            var stageHandshake = new SqlCheckStage
            {
                Name = "SQL Handshake",
                Description = "TDS Protocol negotiation & TLS handshake"
            };
            var stageAuth = new SqlCheckStage
            {
                Name = "Authentication",
                Description = useWindowsAuth ? "Integrated Windows NT Authentication" : $"SQL Server Auth (User: {username})"
            };
            var stageDb = new SqlCheckStage
            {
                Name = "Database Access",
                Description = $"Connecting to catalog '{result.SummaryDatabase}'"
            };

            result.Checks.Add(stageHandshake);
            result.Checks.Add(stageAuth);
            result.Checks.Add(stageDb);

            var builder = new SqlConnectionStringBuilder
            {
                DataSource = BuildDataSource(server, targetPort),
                InitialCatalog = result.SummaryDatabase,
                ConnectTimeout = connectTimeout,
                CommandTimeout = commandTimeout,
                Encrypt = encrypt ? SqlConnectionEncryptOption.Mandatory : SqlConnectionEncryptOption.Optional,
                TrustServerCertificate = trustCertificate,
                ApplicationName = string.IsNullOrWhiteSpace(applicationName) ? "SupportToolKit" : applicationName
            };

            if (useWindowsAuth)
            {
                builder.IntegratedSecurity = true;
            }
            else
            {
                builder.IntegratedSecurity = false;
                builder.UserID = username;
                builder.Password = password;
            }

            sw.Restart();
            SqlConnection? conn = null;
            try
            {
                stageHandshake.Status = "Running";
                stageAuth.Status = "Running";
                stageDb.Status = "Running";

                conn = new SqlConnection(builder.ConnectionString);
                await conn.OpenAsync(ct);
                sw.Stop();

                long openTime = Math.Max(2, sw.ElapsedMilliseconds);
                stageHandshake.LatencyMs = (long)(openTime * 0.5);
                stageHandshake.Status = "Success";
                stageHandshake.Details = "TDS Handshake established";

                stageAuth.LatencyMs = (long)(openTime * 0.3);
                stageAuth.Status = "Success";
                stageAuth.Details = useWindowsAuth ? "Windows identity validated" : "Login credentials accepted";

                stageDb.LatencyMs = (long)(openTime * 0.2);
                stageDb.Status = "Success";
                stageDb.Details = $"Active context: {conn.Database}";

                result.Success = true;
                result.OverallStatus = "Healthy";

                // Retrieve Server Info & Health Metrics
                await PopulateServerInfoAndHealthAsync(conn, result, ct);
            }
            catch (SqlException sqlEx)
            {
                sw.Stop();
                result.Success = false;
                result.TechnicalDetails = new SqlTechnicalDetails
                {
                    ExceptionType = "SqlException",
                    ErrorNumber = sqlEx.Number,
                    State = sqlEx.State,
                    Class = sqlEx.Class,
                    ServerTarget = server,
                    Message = sqlEx.Message,
                    StackTrace = sqlEx.StackTrace
                };

                ClassifySqlError(sqlEx, result, stageHandshake, stageAuth, stageDb, targetPort, useWindowsAuth, username);
            }
            catch (Exception ex)
            {
                sw.Stop();
                result.Success = false;
                stageHandshake.Status = stageHandshake.Status == "Running" ? "Failed" : stageHandshake.Status;
                stageAuth.Status = stageAuth.Status == "Running" ? "Skipped" : stageAuth.Status;
                stageDb.Status = stageDb.Status == "Running" ? "Skipped" : stageDb.Status;

                result.OverallStatus = "Connection Error";
                result.FailureSummary = ex.Message;
                result.PossibleCauses.Add("An unexpected network or socket error occurred.");
                result.TechnicalDetails = new SqlTechnicalDetails
                {
                    ExceptionType = ex.GetType().Name,
                    Message = ex.Message,
                    ServerTarget = server,
                    StackTrace = ex.StackTrace
                };
            }
            finally
            {
                if (conn != null)
                {
                    await conn.DisposeAsync();
                }
            }

            overallSw.Stop();
            result.TotalLatencyMs = overallSw.ElapsedMilliseconds;
            return result;
        }, ct);
    }

    private static async Task PopulateServerInfoAndHealthAsync(SqlConnection conn, SqlDiagnosticResult result, CancellationToken ct)
    {
        try
        {
            result.ServerInfo.Version = conn.ServerVersion;
            result.ServerInfo.ServiceStatus = "Running";

            // 1. Query Server Properties
            using var cmdInfo = new SqlCommand(@"
                SELECT 
                    CAST(SERVERPROPERTY('ProductVersion') AS VARCHAR(64)) AS ProductVersion,
                    CAST(SERVERPROPERTY('ProductLevel') AS VARCHAR(64)) AS ProductLevel,
                    CAST(SERVERPROPERTY('Edition') AS VARCHAR(128)) AS Edition,
                    CAST(SERVERPROPERTY('InstanceName') AS VARCHAR(64)) AS InstanceName,
                    CAST(SERVERPROPERTY('Collation') AS VARCHAR(64)) AS Collation,
                    @@VERSION AS FullVersion;
            ", conn);

            using var reader = await cmdInfo.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
            {
                string prodVer = reader["ProductVersion"]?.ToString() ?? "";
                string prodLvl = reader["ProductLevel"]?.ToString() ?? "";
                string edition = reader["Edition"]?.ToString() ?? "";
                string instName = reader["InstanceName"]?.ToString() ?? "";
                string collation = reader["Collation"]?.ToString() ?? "";
                string fullVer = reader["FullVersion"]?.ToString() ?? "";

                result.ServerInfo.Version = $"{prodVer} {prodLvl}".Trim();
                result.ServerInfo.Edition = edition;
                result.ServerInfo.InstanceName = string.IsNullOrEmpty(instName) ? "MSSQLSERVER (Default)" : instName;
                result.ServerInfo.Collation = collation;

                if (fullVer.Contains("Windows")) result.ServerInfo.OperatingSystem = "Windows";
                else if (fullVer.Contains("Linux")) result.ServerInfo.OperatingSystem = "Linux";
            }
            await reader.CloseAsync();
        }
        catch { }

        // 2. Query Server Uptime from sys.dm_os_sys_info
        try
        {
            using var cmdUptime = new SqlCommand(@"
                SELECT DATEDIFF(SECOND, sqlserver_start_time, GETDATE()) AS UptimeSec
                FROM sys.dm_os_sys_info;
            ", conn);

            var uptimeObj = await cmdUptime.ExecuteScalarAsync(ct);
            if (uptimeObj != null && long.TryParse(uptimeObj.ToString(), out long sec))
            {
                var ts = TimeSpan.FromSeconds(sec);
                result.ServerInfo.ServerUptime = ts.Days > 0 
                    ? $"{ts.Days}d {ts.Hours}h {ts.Minutes}m" 
                    : $"{ts.Hours}h {ts.Minutes}m {ts.Seconds}s";
            }
        }
        catch { }

        // 3. Query Real Health Metrics from DMVs
        try
        {
            using var cmdHealth = new SqlCommand(@"
                SELECT 
                    (SELECT COUNT(*) FROM sys.dm_exec_sessions WHERE is_user_process = 1) AS ActiveConnections,
                    (SELECT COUNT(*) FROM sys.databases) AS DatabaseCount,
                    (SELECT COUNT(*) FROM sys.dm_exec_requests WHERE blocking_session_id != 0) AS BlockedProcesses;
            ", conn);

            using var readerHealth = await cmdHealth.ExecuteReaderAsync(ct);
            if (await readerHealth.ReadAsync(ct))
            {
                result.ServerHealth.ActiveConnections = readerHealth["ActiveConnections"]?.ToString() ?? "N/A";
                result.ServerHealth.DatabaseCount = readerHealth["DatabaseCount"]?.ToString() ?? "N/A";
                result.ServerHealth.BlockedProcesses = readerHealth["BlockedProcesses"]?.ToString() ?? "0";
            }
            await readerHealth.CloseAsync();
        }
        catch { }

        // 4. Query TempDB Space Usage
        try
        {
            using var cmdTempDb = new SqlCommand(@"
                SELECT 
                    CAST(ROUND((SUM(user_object_reserved_page_count + internal_object_reserved_page_count) * 8.0 / 1024.0), 1) AS VARCHAR(32)) AS TempDbUsedMb
                FROM tempdb.sys.dm_db_file_space_usage;
            ", conn);

            var tempDbMb = await cmdTempDb.ExecuteScalarAsync(ct);
            if (tempDbMb != null)
            {
                result.ServerHealth.TempDbUsage = $"{tempDbMb} MB";
            }
        }
        catch { }

        // 5. Query Memory Usage
        try
        {
            using var cmdMem = new SqlCommand(@"
                SELECT CAST(ROUND(physical_memory_in_use_kb / 1024.0, 1) AS VARCHAR(32)) AS MemUsedMb
                FROM sys.dm_os_process_memory;
            ", conn);

            var memMb = await cmdMem.ExecuteScalarAsync(ct);
            if (memMb != null)
            {
                result.ServerHealth.MemoryUsage = $"{memMb} MB";
            }
        }
        catch { }
    }

    private static void ClassifySqlError(
        SqlException ex,
        SqlDiagnosticResult result,
        SqlCheckStage stageHandshake,
        SqlCheckStage stageAuth,
        SqlCheckStage stageDb,
        int port,
        bool useWindowsAuth,
        string username)
    {
        // SQL Server Specific Error Numbers
        switch (ex.Number)
        {
            case 18456: // Login failed for user
                stageHandshake.Status = "Success";
                stageAuth.Status = "Failed";
                stageAuth.Details = $"Login failed (Error 18456)";
                stageDb.Status = "Skipped";

                result.OverallStatus = "Authentication Failed";
                result.FailureSummary = useWindowsAuth
                    ? "Windows Authentication failed. The current Windows user account does not have login permissions on this SQL Server instance."
                    : $"SQL Server Authentication failed for user '{username}'. Invalid username or password.";

                result.PossibleCauses.AddRange(new[]
                {
                    useWindowsAuth 
                        ? "Current Windows user is not mapped to a SQL Server login." 
                        : "Incorrect username or password.",
                    "SQL Server instance may be configured for 'Windows Authentication Only' mode (Mixed Mode disabled).",
                    "The SQL login account may be disabled or locked out.",
                    "Password has expired or must be changed."
                });
                break;

            case 4060: // Cannot open database requested by the login
                stageHandshake.Status = "Success";
                stageAuth.Status = "Success";
                stageDb.Status = "Failed";
                stageDb.Details = $"Database '{result.SummaryDatabase}' inaccessible (Error 4060)";

                result.OverallStatus = "Database Inaccessible";
                result.FailureSummary = $"Login succeeded, but the database '{result.SummaryDatabase}' does not exist or the user has no permissions.";
                result.PossibleCauses.AddRange(new[]
                {
                    $"Database '{result.SummaryDatabase}' does not exist on this SQL Server instance.",
                    "The user has not been granted database access or 'CONNECT' permission.",
                    "The database is in Single-User, Restoring, or Offline mode."
                });
                break;

            case -1: // General Connection Timeout / Network
            case 2:  // An error has occurred while establishing a connection to the server
            case 53: // The network path was not found
            case 10060: // A connection attempt failed because the connected party did not properly respond
            case 10061: // Target machine actively refused it
                stageHandshake.Status = "Failed";
                stageHandshake.Details = $"TCP / Instance Connection Failed (Error {ex.Number})";
                stageAuth.Status = "Skipped";
                stageDb.Status = "Skipped";

                result.OverallStatus = "Instance / Port Unreachable";
                result.FailureSummary = $"Could not establish network connection to SQL Server on port {port}.";
                result.PossibleCauses.AddRange(new[]
                {
                    "SQL Server service (MSSQLSERVER or instance service) is not running.",
                    "TCP/IP protocol is Disabled in SQL Server Configuration Manager.",
                    $"Windows Firewall is blocking TCP port {port} on the server.",
                    "Incorrect instance name (e.g. \\SQLEXPRESS missing or misspelled).",
                    "If using named instances, SQL Server Browser service (UDP 1434) may be stopped.",
                    "SQL Server is configured to listen on a dynamic port instead of 1433."
                });
                break;

            default:
                stageHandshake.Status = "Failed";
                stageHandshake.Details = $"SQL Error {ex.Number}";
                stageAuth.Status = "Skipped";
                stageDb.Status = "Skipped";

                result.OverallStatus = $"SQL Error {ex.Number}";
                result.FailureSummary = ex.Message;
                result.PossibleCauses.AddRange(new[]
                {
                    "Verify server instance name and parameters.",
                    "Check SQL Server Error Log on the host for details.",
                    "Ensure network protocols and certificates are configured properly."
                });
                break;
        }
    }

    private static string ExtractHostOnly(string server)
    {
        if (string.IsNullOrWhiteSpace(server)) return "127.0.0.1";
        var s = server.Trim();

        // Check comma format: 192.168.1.50,1433
        int commaIdx = s.IndexOf(',');
        if (commaIdx > 0) s = s[..commaIdx];

        // Check slash format: 192.168.1.50\SQLEXPRESS
        int slashIdx = s.IndexOf('\\');
        if (slashIdx > 0) s = s[..slashIdx];

        return s.Trim();
    }

    private static string BuildDataSource(string server, int port)
    {
        var s = (server ?? "").Trim();
        if (string.IsNullOrEmpty(s)) s = "127.0.0.1";

        // If port is specified and not already in string, and not a named instance
        if (port > 0 && port != 1433 && !s.Contains(',') && !s.Contains('\\'))
        {
            return $"{s},{port}";
        }

        return s;
    }

    // ========================================================
    // Profile Management (DPAPI Protected)
    // ========================================================
    public async Task<List<SqlSavedProfile>> LoadProfilesAsync()
    {
        return await Task.Run(() =>
        {
            try
            {
                if (!File.Exists(ProfilesFile)) return [];
                var json = File.ReadAllText(ProfilesFile);
                var list = JsonSerializer.Deserialize<List<SqlSavedProfile>>(json) ?? [];
                return list;
            }
            catch { return []; }
        });
    }

    public async Task SaveProfileAsync(SqlSavedProfile profile)
    {
        await Task.Run(() =>
        {
            var list = File.Exists(ProfilesFile)
                ? JsonSerializer.Deserialize<List<SqlSavedProfile>>(File.ReadAllText(ProfilesFile)) ?? []
                : [];

            var existing = list.FirstOrDefault(p => p.Id == profile.Id);
            if (existing != null)
            {
                list.Remove(existing);
            }
            list.Insert(0, profile);

            var dir = Path.GetDirectoryName(ProfilesFile);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllText(ProfilesFile, JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true }));
        });
    }

    public async Task DeleteProfileAsync(string id)
    {
        await Task.Run(() =>
        {
            if (!File.Exists(ProfilesFile)) return;
            var list = JsonSerializer.Deserialize<List<SqlSavedProfile>>(File.ReadAllText(ProfilesFile)) ?? [];
            list.RemoveAll(p => p.Id == id);
            File.WriteAllText(ProfilesFile, JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true }));
        });
    }

    // ========================================================
    // History Management
    // ========================================================
    public async Task<List<SqlTestHistoryItem>> LoadHistoryAsync()
    {
        return await Task.Run(() =>
        {
            try
            {
                if (!File.Exists(HistoryFile)) return [];
                var json = File.ReadAllText(HistoryFile);
                return JsonSerializer.Deserialize<List<SqlTestHistoryItem>>(json) ?? [];
            }
            catch { return []; }
        });
    }

    public async Task SaveHistoryItemAsync(SqlTestHistoryItem item)
    {
        await Task.Run(() =>
        {
            var list = File.Exists(HistoryFile)
                ? JsonSerializer.Deserialize<List<SqlTestHistoryItem>>(File.ReadAllText(HistoryFile)) ?? []
                : [];

            list.Insert(0, item);
            if (list.Count > 50) list = list.Take(50).ToList();

            var dir = Path.GetDirectoryName(HistoryFile);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllText(HistoryFile, JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true }));
        });
    }

    public async Task ClearHistoryAsync()
    {
        await Task.Run(() =>
        {
            if (File.Exists(HistoryFile))
                File.Delete(HistoryFile);
        });
    }

    // ========================================================
    // DPAPI Security Helpers
    // ========================================================
    public string EncryptPassword(string plain)
    {
        if (string.IsNullOrEmpty(plain)) return "";
        try
        {
            var bytes = Encoding.UTF8.GetBytes(plain);
            var encrypted = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(encrypted);
        }
        catch { return ""; }
    }

    public string DecryptPassword(string? encrypted)
    {
        if (string.IsNullOrEmpty(encrypted)) return "";
        try
        {
            var bytes = Convert.FromBase64String(encrypted);
            var decrypted = ProtectedData.Unprotect(bytes, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(decrypted);
        }
        catch { return ""; }
    }
}
