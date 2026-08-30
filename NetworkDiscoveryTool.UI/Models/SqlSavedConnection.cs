namespace NetworkDiscoveryTool.UI.Models;

public sealed class SqlSavedConnection
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = "";
    public string ServerName { get; set; } = "";
    public string Database { get; set; } = "";
    public bool UseWindowsAuth { get; set; } = true;
    public string Username { get; set; } = "";
    public string? EncryptedPassword { get; set; }
    public DateTime LastUsed { get; set; } = DateTime.Now;
}

public sealed class SqlTestResult
{
    public bool Success { get; set; }
    public string Status { get; set; } = "";
    public string ServerVersion { get; set; } = "";
    public string DatabaseVersion { get; set; } = "";
    public long LatencyMs { get; set; }
    public int DatabaseCount { get; set; }
    public string ErrorMessage { get; set; } = "";
}
