using System.ComponentModel.DataAnnotations;

namespace NetworkDiscoveryTool.Core.Models;

public sealed class AppSetting
{
    [Key]
    [MaxLength(200)]
    public string Key { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;
}
