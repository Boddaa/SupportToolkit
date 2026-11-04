using System;
using System.Threading;
using System.Threading.Tasks;
using NetworkDiscoveryTool.UI.Models;

namespace NetworkDiscoveryTool.UI.Services;

public interface IProcessMonitorService : IDisposable
{
    event Action<SystemPerformanceSnapshot>? SnapshotUpdated;
    bool IsMonitoring { get; }
    TimeSpan Interval { get; set; }
    void StartMonitoring();
    void StopMonitoring();
    Task<SystemPerformanceSnapshot> CollectSnapshotAsync(CancellationToken ct = default);
    System.Collections.Generic.List<StartupItemModel> GetStartupItems();
}
