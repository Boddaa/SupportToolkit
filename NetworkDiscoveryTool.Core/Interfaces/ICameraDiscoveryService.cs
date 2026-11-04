using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NetworkDiscoveryTool.Core.Models;

namespace NetworkDiscoveryTool.Core.Interfaces;

public interface ICameraDiscoveryService
{
    Task<List<DiscoveredCamera>> DiscoverCamerasAsync(
        string? localInterfaceIp = null,
        IProgress<string>? statusProgress = null,
        CancellationToken ct = default);
}

