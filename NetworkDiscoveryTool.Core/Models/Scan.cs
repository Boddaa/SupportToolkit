using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NetworkDiscoveryTool.Core.Models;

public sealed class Scan
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    public DateTime Date { get; set; }

    [MaxLength(45)]
    public string StartIP { get; set; } = string.Empty;

    [MaxLength(45)]
    public string EndIP { get; set; } = string.Empty;

    public long DurationMs { get; set; }

    public string? Description { get; set; }

    public string? Notes { get; set; }

    public int? UserId { get; set; }

    public User? User { get; set; }

    public ICollection<Device> Devices { get; set; } = new List<Device>();
}
