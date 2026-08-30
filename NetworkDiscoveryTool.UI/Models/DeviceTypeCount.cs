using CommunityToolkit.Mvvm.ComponentModel;

namespace NetworkDiscoveryTool.UI.Models;

public sealed partial class DeviceTypeCount : ObservableObject
{
    [ObservableProperty]
    private string _deviceType = string.Empty;

    [ObservableProperty]
    private int _count;

    [ObservableProperty]
    private double _percentage;
}
