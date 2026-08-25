using Microsoft.EntityFrameworkCore;
using ParentalTrack.Domain.Entities;

namespace ParentalTrack.Infrastructure.Persistence;

/// <summary>
/// The single EF Core context for the whole monolith. Mapping lives entirely in
/// <c>Persistence/Configurations</c>; nothing here relies on naming conventions.
/// </summary>
public sealed class AppDbContext : DbContext
{
    /// <summary>Creates the context with the options supplied by dependency injection.</summary>
    /// <param name="options">Provider and connection configuration.</param>
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    /// <summary>Parent accounts (<c>parents</c>).</summary>
    public DbSet<Parent> Parents => Set<Parent>();

    /// <summary>Issued parent refresh tokens (<c>refresh_tokens</c>).</summary>
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    /// <summary>Enrolled child devices (<c>child_devices</c>).</summary>
    public DbSet<ChildDevice> ChildDevices => Set<ChildDevice>();

    /// <summary>Issued device tokens (<c>device_sessions</c>).</summary>
    public DbSet<DeviceSession> DeviceSessions => Set<DeviceSession>();

    /// <summary>Uploaded location fixes (<c>location_records</c>).</summary>
    public DbSet<LocationRecord> LocationRecords => Set<LocationRecord>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
