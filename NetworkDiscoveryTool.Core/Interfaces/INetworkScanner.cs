using NetworkDiscoveryTool.Core.Models;

namespace NetworkDiscoveryTool.Core.Interfaces;

public interface INetworkScanner
{
    Task<List<Device>> ScanRangeAsync(
        string startIP, string endIP,
        int maxThreads = 100,
        int pingTimeoutMs = 2000,
        int portTimeoutMs = 500,
        string? description = null,
        string? notes = null,
        int? userId = null,
        IProgress<int>? progress = null,
        CancellationToken ct = default);
}
