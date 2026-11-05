using Microsoft.EntityFrameworkCore;
using NetworkDiscoveryTool.Server.Models;

namespace NetworkDiscoveryTool.Server.Data;

public sealed class ServerDbContext : DbContext
{
    public DbSet<ServerUser> Users => Set<ServerUser>();
    public DbSet<DeviceRegistration> Devices => Set<DeviceRegistration>();
    public DbSet<UserDevice> UserDevices => Set<UserDevice>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    public ServerDbContext(DbContextOptions<ServerDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ServerUser>(entity =>
        {
            entity.ToTable("Users");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Username).IsRequired().HasMaxLength(100);
            entity.HasIndex(e => e.Username).IsUnique();
        });

        modelBuilder.Entity<DeviceRegistration>(entity =>
        {
            entity.ToTable("Devices");
            entity.HasKey(e => e.HardwareId);
            entity.Property(e => e.HardwareId).IsRequired().HasMaxLength(100);
        });

        modelBuilder.Entity<UserDevice>(entity =>
        {
            entity.ToTable("UserDevices");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.UserId, e.HardwareId });
        });

        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.ToTable("AuditLogs");
            entity.HasKey(e => e.Id);
        });
    }
}
