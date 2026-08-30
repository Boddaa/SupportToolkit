using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NetworkDiscoveryTool.Core.Models;

public sealed class OperationLog
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    [Required]
    [MaxLength(100)]
    public string OperationName { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Description { get; set; }

    [Required]
    public DateTime Timestamp { get; set; }

    [MaxLength(50)]
    public string? Result { get; set; } = "Success";

    public long DurationMs { get; set; }

    [MaxLength(100)]
    public string? Username { get; set; }
}
