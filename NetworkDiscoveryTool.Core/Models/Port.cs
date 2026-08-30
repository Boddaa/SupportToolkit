using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NetworkDiscoveryTool.Core.Models;

public sealed class Port
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    public int DeviceId { get; set; }

    public int PortNumber { get; set; }

    [MaxLength(20)]
    public string State { get; set; } = "Closed";

    [MaxLength(50)]
    public string? Service { get; set; }

    [ForeignKey(nameof(DeviceId))]
    public Device Device { get; set; } = null!;
}
