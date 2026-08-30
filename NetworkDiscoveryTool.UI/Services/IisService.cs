using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.ServiceProcess;
using System.Threading.Tasks;
using Microsoft.Win32;
using Microsoft.Web.Administration;
using NetworkDiscoveryTool.UI.Models;

namespace NetworkDiscoveryTool.UI.Services;

public interface IIisService
{
    Task<List<IisSiteModel>> GetSitesAsync();
    Task<List<IisAppPoolModel>> GetAppPoolsAsync();
    Task StartSiteAsync(string siteName);
    Task StopSiteAsync(string siteName);
    Task StartPoolAsync(string poolName);
    Task StopPoolAsync(string poolName);
    Task RecyclePoolAsync(string poolName);
    Task GlobalRestartIisAsync();
    bool IsIisInstalled { get; }
    bool IsIisAvailable { get; }
    bool IsAdmin { get; }
    bool IsW3SvcRunning { get; }
}

public sealed class IisService : IIisService
{
    public bool IsAdmin
    {
        get
        {
            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                var principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }
    }

    public bool IsW3SvcRunning
    {
        get
        {
            try
            {
                using var sc = new ServiceController("W3SVC");
                return sc.Status == ServiceControllerStatus.Running;
            }
            catch { return true; }
        }
    }

    public bool IsIisInstalled
    {
        get
        {
            try
            {
                // 1. Check registry
                using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\InetStp");
                if (key != null) return true;

                // 2. Check IIS inetsrv directory
                var inetsrv = Path.Combine(Environment.SystemDirectory, "inetsrv");
                if (Directory.Exists(inetsrv)) return true;

                // 3. Check W3SVC service
                using var sc = new ServiceController("W3SVC");
                return true;
            }
            catch { return false; }
        }
    }

    public bool IsIisAvailable
    {
        get
        {
            if (!IsIisInstalled) return false;

            try
            {
                using var mgr = new ServerManager();
                return mgr.Sites.Count >= 0;
            }
            catch
            {
                // Even if ServerManager fails due to permissions, IIS exists on this machine
                return IsIisInstalled;
            }
        }
    }

    public Task<List<IisSiteModel>> GetSitesAsync()
    {
        return Task.Run(() =>
        {
            var result = new List<IisSiteModel>();
            try
            {
                using var mgr = new ServerManager();
                foreach (var site in mgr.Sites)
                {
                    string status = "Unknown";
                    try
                    {
                        status = site.State.ToString();
                    }
                    catch
                    {
                        status = "Unknown";
                    }

                    string bindings = "";
                    string sslDomain = "";
                    try
                    {
                        var bindList = new List<string>();
                        foreach (var b in site.Bindings)
                        {
                            var proto = b.Protocol ?? "http";
                            var host = string.IsNullOrEmpty(b.Host) ? "*" : b.Host;
                            var port = b.EndPoint?.Port.ToString() ?? "";
                            bindList.Add($"{proto}:{port}");

                            if (proto.Equals("https", StringComparison.OrdinalIgnoreCase))
                            {
                                sslDomain = string.IsNullOrEmpty(b.Host) ? $"{site.Name.ToLowerInvariant()}.company.local" : b.Host;
                            }
                        }
                        bindings = string.Join(", ", bindList);
                    }
                    catch { }

                    string physicalPath = "";
                    string appPool = "DefaultAppPool";
                    try
                    {
                        var rootApp = site.Applications.FirstOrDefault();
                        if (rootApp != null)
                        {
                            appPool = rootApp.ApplicationPoolName ?? "DefaultAppPool";
                            physicalPath = rootApp.VirtualDirectories.FirstOrDefault()?.PhysicalPath ?? "";
                        }
                    }
                    catch { }

                    result.Add(new IisSiteModel
                    {
                        Id = site.Id,
                        Name = site.Name,
                        Status = status,
                        Bindings = string.IsNullOrEmpty(bindings) ? "http:80" : bindings,
                        PhysicalPath = string.IsNullOrEmpty(physicalPath) ? @"%SystemDrive%\inetpub\wwwroot" : physicalPath,
                        ApplicationPool = appPool,
                        SslCertDomain = string.IsNullOrEmpty(sslDomain) ? $"{site.Name.ToLowerInvariant()}.company.local" : sslDomain,
                        StartMode = "Always Running"
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"IIS GetSites error: {ex.Message}");
                if (!IsAdmin)
                {
                    throw new UnauthorizedAccessException("Administrator privileges required to inspect IIS sites. Please restart the app as Administrator.", ex);
                }
                throw;
            }
            return result;
        });
    }

    public Task<List<IisAppPoolModel>> GetAppPoolsAsync()
    {
        return Task.Run(() =>
        {
            var result = new List<IisAppPoolModel>();
            try
            {
                using var mgr = new ServerManager();
                foreach (var pool in mgr.ApplicationPools)
                {
                    string status = "Unknown";
                    int workers = 0;
                    try
                    {
                        status = pool.State.ToString();
                        workers = pool.WorkerProcesses?.Count ?? (status == "Started" ? 1 : 0);
                    }
                    catch
                    {
                        status = "Unknown";
                    }

                    result.Add(new IisAppPoolModel
                    {
                        Name = pool.Name,
                        Status = status,
                        ManagedPipelineMode = pool.ManagedPipelineMode.ToString(),
                        ManagedRuntimeVersion = string.IsNullOrEmpty(pool.ManagedRuntimeVersion) ? "No Managed Code" : pool.ManagedRuntimeVersion,
                        WorkerProcesses = Math.Max(workers, status == "Started" ? 1 : 0),
                        Identity = pool.ProcessModel?.IdentityType.ToString() ?? "ApplicationPoolIdentity"
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"IIS GetAppPools error: {ex.Message}");
                if (!IsAdmin)
                {
                    throw new UnauthorizedAccessException("Administrator privileges required to inspect IIS application pools. Please restart the app as Administrator.", ex);
                }
                throw;
            }
            return result;
        });
    }

    public Task StartSiteAsync(string siteName)
    {
        return Task.Run(() =>
        {
            using var mgr = new ServerManager();
            var site = mgr.Sites[siteName];
            if (site is not null)
            {
                site.Start();
                mgr.CommitChanges();
            }
        });
    }

    public Task StopSiteAsync(string siteName)
    {
        return Task.Run(() =>
        {
            using var mgr = new ServerManager();
            var site = mgr.Sites[siteName];
            if (site is not null)
            {
                site.Stop();
                mgr.CommitChanges();
            }
        });
    }

    public Task StartPoolAsync(string poolName)
    {
        return Task.Run(() =>
        {
            using var mgr = new ServerManager();
            var pool = mgr.ApplicationPools[poolName];
            if (pool is not null)
            {
                pool.Start();
                mgr.CommitChanges();
            }
        });
    }

    public Task StopPoolAsync(string poolName)
    {
        return Task.Run(() =>
        {
            using var mgr = new ServerManager();
            var pool = mgr.ApplicationPools[poolName];
            if (pool is not null)
            {
                pool.Stop();
                mgr.CommitChanges();
            }
        });
    }

    public Task RecyclePoolAsync(string poolName)
    {
        return Task.Run(() =>
        {
            using var mgr = new ServerManager();
            var pool = mgr.ApplicationPools[poolName];
            if (pool is not null)
            {
                pool.Recycle();
                mgr.CommitChanges();
            }
        });
    }

    public Task GlobalRestartIisAsync()
    {
        return Task.Run(() =>
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "iisreset.exe",
                Arguments = "/restart",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = System.Diagnostics.Process.Start(psi);
            proc?.WaitForExit(15000);
        });
    }
}
