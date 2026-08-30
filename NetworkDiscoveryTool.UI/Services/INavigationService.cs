using System.Windows.Controls;

namespace NetworkDiscoveryTool.UI.Services;

public interface INavigationService
{
    void SetFrame(Frame frame);
    void NavigateToDashboard();
    void NavigateToNetworkDiscovery();
    void NavigateToLiveTopology();
    void NavigateToPingTool();
    void NavigateToPortChecker();
    void NavigateToSystemInfo();
    void NavigateToWindowsServices();
    void NavigateToSqlTester();
    void NavigateToIisMonitor();
    void NavigateToLogCollector();
    void NavigateToScreenshot();
    void NavigateToOperationHistory();
    void NavigateToSettings();
    void NavigateToDeviceDetails(int deviceId);
    void NavigateToDeviceDetails(string ip);
}
