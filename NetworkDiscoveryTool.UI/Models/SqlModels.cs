using System;
using System.Collections.Generic;

namespace NetworkDiscoveryTool.UI.Models;

public sealed class SqlSavedProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = "";
    public string Server { get; set; } = "";
    public int Port { get; set; } = 1433;
    public string Database { get; set; } = "master";
    public bool UseWindowsAuth { get; set; } = true;
    public string Username { get; set; } = "";
    public string? EncryptedPassword { get; set; }
    public int ConnectTimeout { get; set; } = 15;
    public int CommandTimeout { get; set; } = 30;
    public bool Encrypt { get; set; } = false;
    public bool TrustCertificate { get; set; } = true;
    public DateTime? LastTested { get; set; }
    public long LastLatencyMs { get; set; }
    public string LastStatus { get; set; } = "Not Tested";
    public bool IsHealthy { get; set; }
}

public sealed class SqlTestHistoryItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public string TimeFormatted => Timestamp.ToString("HH:mm:ss");
    public string DateFormatted => Timestamp.ToString("yyyy-MM-dd");
    public string Server { get; set; } = "";
    public string Database { get; set; } = "master";
    public string AuthType { get; set; } = "Windows";
    public bool Success { get; set; }
    public long LatencyMs { get; set; }
    public string LatencyText => Success ? $"{LatencyMs} ms" : "-";
    public string ResultText => Success ? "Success" : "Failed";
    public string ResultColor => Success ? "#10B981" : "#EF4444";
    public string StageFailed { get; set; } = "";
    public string ErrorMessage { get; set; } = "";
    public SqlDiagnosticResult? DiagnosticSnapshot { get; set; }
}

public sealed class SqlCheckStage
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Status { get; set; } = "Pending"; // "Success", "Failed", "Skipped", "Running"
    public long? LatencyMs { get; set; }
    public string LatencyText => LatencyMs.HasValue ? $"{LatencyMs.Value} ms" : "";
    public string Details { get; set; } = "";
    public string StatusIcon => Status switch
    {
        "Success" => "✓",
        "Failed" => "✕",
        "Running" => "●",
        _ => "—"
    };
    public string StatusColor => Status switch
    {
        "Success" => "#10B981",
        "Failed" => "#EF4444",
        "Running" => "#0EA5E9",
        _ => "#64748B"
    };
}

public sealed class SqlServerInfo
{
    public string Version { get; set; } = "Unavailable";
    public string Edition { get; set; } = "Unavailable";
    public string OperatingSystem { get; set; } = "Unavailable";
    public string InstanceName { get; set; } = "Unavailable";
    public string ServerUptime { get; set; } = "Unavailable";
    public string ServiceStatus { get; set; } = "Running";
    public string TcpPort { get; set; } = "1433";
    public string CurrentDatabase { get; set; } = "master";
    public string Collation { get; set; } = "Unavailable";
}

public sealed class SqlServerHealth
{
    public string CpuUsage { get; set; } = "N/A";
    public string MemoryUsage { get; set; } = "N/A";
    public string ActiveConnections { get; set; } = "N/A";
    public string DatabaseCount { get; set; } = "N/A";
    public string BlockedProcesses { get; set; } = "N/A";
    public string TempDbUsage { get; set; } = "N/A";
}

public sealed class SqlTechnicalDetails
{
    public string ExceptionType { get; set; } = "";
    public int ErrorNumber { get; set; }
    public byte State { get; set; }
    public byte Class { get; set; }
    public string ServerTarget { get; set; } = "";
    public string Message { get; set; } = "";
    public string? StackTrace { get; set; }
}

public sealed class SqlDiagnosticResult
{
    public bool Success { get; set; }
    public string OverallStatus { get; set; } = "Ready to test connection";
    public long TotalLatencyMs { get; set; }
    public string SummaryServer { get; set; } = "";
    public string SummaryDatabase { get; set; } = "master";
    public string SummaryAuth { get; set; } = "Windows";
    public string SummaryResponseTime => $"{TotalLatencyMs} ms";

    public List<SqlCheckStage> Checks { get; set; } = [];
    public string FailureSummary { get; set; } = "";
    public List<string> PossibleCauses { get; set; } = [];
    public SqlTechnicalDetails? TechnicalDetails { get; set; }
    public SqlServerInfo ServerInfo { get; set; } = new();
    public SqlServerHealth ServerHealth { get; set; } = new();
}
