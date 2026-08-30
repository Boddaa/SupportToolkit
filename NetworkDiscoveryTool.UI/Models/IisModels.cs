namespace NetworkDiscoveryTool.UI.Models;

public sealed class IisSiteModel
{
    public string Name { get; set; } = "";
    public long Id { get; set; }
    public string Status { get; set; } = "";
    public string Bindings { get; set; } = "";
    public string PhysicalPath { get; set; } = "";
    public string ApplicationPool { get; set; } = "";
    public string StartMode { get; set; } = "Always Running";
    public string CreatedDate { get; set; } = "5/10/2024 10:15 AM";
    public string LastStartedDate { get; set; } = "5/23/2025 09:42 AM";
    public int WorkerProcesses { get; set; } = 1;
    public string MemoryUsageText { get; set; } = "78.4 MB";
    public int MemoryUsagePercent { get; set; } = 25;
    public string SslCertDomain { get; set; } = "";
    public string SslCertIssuedBy { get; set; } = "Company-CA";
    public string SslCertValidTo { get; set; } = "3/10/2026";
    public bool HasSsl => Bindings.Contains("https", StringComparison.OrdinalIgnoreCase);

    public bool IsRunning => string.Equals(Status, "Started", StringComparison.OrdinalIgnoreCase) || string.Equals(Status, "Running", StringComparison.OrdinalIgnoreCase);
    public string StatusColor => IsRunning ? "#10B981" : "#EF4444";
    public string StatusBgColor => IsRunning ? "#1022C55E" : "#10EF4444";
}

public sealed class IisAppPoolModel
{
    public string Name { get; set; } = "";
    public string Status { get; set; } = "";
    public string ManagedPipelineMode { get; set; } = "";
    public string ManagedRuntimeVersion { get; set; } = "";
    public int WorkerProcesses { get; set; } = 1;
    public string Identity { get; set; } = "ApplicationPoolIdentity";

    public bool IsRunning => string.Equals(Status, "Started", StringComparison.OrdinalIgnoreCase) || string.Equals(Status, "Running", StringComparison.OrdinalIgnoreCase);
    public string StatusColor => IsRunning ? "#10B981" : "#EF4444";
    public string StatusBgColor => IsRunning ? "#1022C55E" : "#10EF4444";
}

