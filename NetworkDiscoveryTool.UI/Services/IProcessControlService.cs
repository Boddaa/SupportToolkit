using System.Diagnostics;
using System.Threading.Tasks;
using NetworkDiscoveryTool.UI.Models;

namespace NetworkDiscoveryTool.UI.Services;

public interface IProcessControlService
{
    Task<(bool Success, string Message)> TerminateProcessAsync(int pid);
    Task<(bool Success, string Message)> TerminateProcessTreeAsync(int pid);
    void OpenFileLocation(string path);
    void ShowFileProperties(string path);
    void CopyProcessInfo(ProcessItemModel process);
    void SearchOnline(string processName);
    Task<(bool Success, string Message)> SetProcessPriorityAsync(int pid, ProcessPriorityClass priority);
}
