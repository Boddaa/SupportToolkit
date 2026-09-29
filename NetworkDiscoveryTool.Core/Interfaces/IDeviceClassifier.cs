using NetworkDiscoveryTool.Core.Models;

namespace NetworkDiscoveryTool.Core.Interfaces;

public interface IDeviceClassifier
{
    string Classify(IEnumerable<Port>? ports, string? vendor, string? hostname, string? httpBanner, string? ip);
    string Classify(Device device);
}
