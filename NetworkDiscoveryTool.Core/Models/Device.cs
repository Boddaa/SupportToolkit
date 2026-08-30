using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NetworkDiscoveryTool.Core.Models;

public sealed class Device
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    public int ScanId { get; set; }

    [Required]
    [MaxLength(45)]
    public string IP { get; set; } = string.Empty;

    [MaxLength(255)]
    public string? Hostname { get; set; }

    [MaxLength(17)]
    public string? MAC { get; set; }

    [MaxLength(200)]
    public string? Vendor { get; set; }

    [MaxLength(100)]
    public string? DeviceType { get; set; }

    [MaxLength(20)]
    public string Status { get; set; } = "Offline";

    public long LatencyMs { get; set; }

    [MaxLength(100)]
    public string? OS { get; set; }

    public string? Notes { get; set; }

    public bool IsFavorite { get; set; }

    [ForeignKey(nameof(ScanId))]
    public Scan Scan { get; set; } = null!;

    public ICollection<Port> Ports { get; set; } = new List<Port>();
}
