using System.Windows;
using System.Windows.Controls;
using NetworkDiscoveryTool.UI.ViewModels;

namespace NetworkDiscoveryTool.UI.Views.DeviceDetails;

public partial class DeviceDetailsPage : Page
{
    private readonly DeviceDetailsViewModel _vm;
    private readonly int _deviceId;
    private readonly string? _ip;

    public DeviceDetailsPage(DeviceDetailsViewModel vm, int deviceId)
    {
        _vm = vm;
        _deviceId = deviceId;
        InitializeComponent();
        DataContext = vm;
        Loaded += OnLoaded;
    }

    public DeviceDetailsPage(DeviceDetailsViewModel vm, string ip)
    {
        _vm = vm;
        _ip = ip;
        InitializeComponent();
        DataContext = vm;
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_ip))
            await _vm.LoadDeviceByIpAsync(_ip);
        else
            await _vm.LoadDeviceAsync(_deviceId);
    }
}
