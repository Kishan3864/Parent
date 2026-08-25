using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ParentalTrack.Domain.Entities;

namespace ParentalTrack.Infrastructure.Persistence.Configurations;

/// <summary>Maps <see cref="ChildDevice"/> to <c>child_devices</c>.</summary>
public sealed class ChildDeviceConfiguration : IEntityTypeConfiguration<ChildDevice>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ChildDevice> builder)
    {
        builder.ToTable("child_devices");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id");

        builder.Property(x => x.ParentId)
            .HasColumnName("parent_id");

        builder.Property(x => x.ChildName)
            .HasColumnName("child_name")
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(x => x.DeviceLabel)
            .HasColumnName("device_label")
            .HasMaxLength(128);

        builder.Property(x => x.Platform)
            .HasColumnName("platform")
            .HasMaxLength(32);

        builder.Property(x => x.Manufacturer)
            .HasColumnName("manufacturer")
            .HasMaxLength(64);

        builder.Property(x => x.Model)
            .HasColumnName("model")
            .HasMaxLength(64);

        builder.Property(x => x.OsVersion)
            .HasColumnName("os_version")
            .HasMaxLength(32);

        builder.Property(x => x.AppVersion)
            .HasColumnName("app_version")
            .HasMaxLength(32);

        builder.Property(x => x.InstallId)
            .HasColumnName("install_id")
            .HasMaxLength(64);

        builder.Property(x => x.PairingCodeHash)
            .HasColumnName("pairing_code_hash")
            .HasMaxLength(128);

        builder.Property(x => x.PairingCodeExpiresAt)
            .HasColumnName("pairing_code_expires_at");

        builder.Property(x => x.PairedAt)
            .HasColumnName("paired_at");

        builder.Property(x => x.IsActive)
            .HasColumnName("is_active")
            .HasDefaultValue(true);

        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at");

        builder.Property(x => x.LastSeenAt)
            .HasColumnName("last_seen_at");

        builder.Property(x => x.LastBatteryPercent)
            .HasColumnName("last_battery_percent");

        // Deliberately a plain column: no foreign key, so deleting old location rows never has to
        // cascade or null this out. The index keeps the "newest fix" join cheap.
        builder.Property(x => x.LastLocationId)
            .HasColumnName("last_location_id");

        builder.HasIndex(x => x.ParentId)
            .HasDatabaseName("ix_child_devices_parent_id");

        builder.HasIndex(x => x.LastLocationId)
            .HasDatabaseName("ix_child_devices_last_location_id");

        builder.HasOne<Parent>()
            .WithMany(p => p.Devices)
            .HasForeignKey(x => x.ParentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
