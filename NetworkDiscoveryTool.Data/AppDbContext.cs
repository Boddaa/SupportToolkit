using Microsoft.EntityFrameworkCore;
using NetworkDiscoveryTool.Core.Models;

namespace NetworkDiscoveryTool.Data;

public sealed class AppDbContext : DbContext
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Scan> Scans => Set<Scan>();
    public DbSet<Device> Devices => Set<Device>();
    public DbSet<Port> Ports => Set<Port>();
    public DbSet<AppSetting> AppSettings => Set<AppSetting>();
    public DbSet<OperationLog> OperationLogs => Set<OperationLog>();

    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("Users");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Username).IsRequired().HasMaxLength(100);
            entity.HasIndex(e => e.Username).IsUnique();
        });

        modelBuilder.Entity<Scan>(entity =>
        {
            entity.ToTable("Scans");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Date).IsRequired();
            entity.Property(e => e.StartIP).IsRequired().HasMaxLength(45);
            entity.Property(e => e.EndIP).IsRequired().HasMaxLength(45);

            entity.HasOne(e => e.User)
                  .WithMany(u => u.Scans)
                  .HasForeignKey(e => e.UserId)
                  .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<Device>(entity =>
        {
            entity.ToTable("Devices");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.IP).IsRequired().HasMaxLength(45);
            entity.Property(e => e.Hostname).HasMaxLength(255);
            entity.Property(e => e.MAC).HasMaxLength(17);
            entity.Property(e => e.Vendor).HasMaxLength(200);
            entity.Property(e => e.DeviceType).HasMaxLength(100);
            entity.Property(e => e.Status).HasMaxLength(20);
            entity.Property(e => e.IsFavorite).HasDefaultValue(false);

            entity.HasOne(e => e.Scan)
                  .WithMany(s => s.Devices)
                  .HasForeignKey(e => e.ScanId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Port>(entity =>
        {
            entity.ToTable("Ports");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.State).HasMaxLength(20);
            entity.Property(e => e.Service).HasMaxLength(50);

            entity.HasOne(e => e.Device)
                  .WithMany(d => d.Ports)
                  .HasForeignKey(e => e.DeviceId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AppSetting>(entity =>
        {
            entity.ToTable("AppSettings");
            entity.HasKey(e => e.Key);
            entity.Property(e => e.Key).HasMaxLength(200).IsRequired();
            entity.Property(e => e.Value).IsRequired();
        });

        modelBuilder.Entity<OperationLog>(entity =>
        {
            entity.ToTable("OperationLogs");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.OperationName).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Description).HasMaxLength(500);
            entity.Property(e => e.Result).HasMaxLength(50);
            entity.Property(e => e.Username).HasMaxLength(100);
            entity.HasIndex(e => e.Timestamp);
            entity.HasIndex(e => e.OperationName);
        });
    }
}
