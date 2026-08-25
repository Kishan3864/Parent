using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ParentalTrack.Domain.Entities;

namespace ParentalTrack.Infrastructure.Persistence.Configurations;

/// <summary>Maps <see cref="LocationRecord"/> to <c>location_records</c>.</summary>
public sealed class LocationRecordConfiguration : IEntityTypeConfiguration<LocationRecord>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<LocationRecord> builder)
    {
        builder.ToTable("location_records");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .ValueGeneratedOnAdd()
            .UseIdentityByDefaultColumn();

        builder.Property(x => x.DeviceId)
            .HasColumnName("device_id");

        builder.Property(x => x.ClientId)
            .HasColumnName("client_id");

        builder.Property(x => x.Latitude)
            .HasColumnName("latitude");

        builder.Property(x => x.Longitude)
            .HasColumnName("longitude");

        builder.Property(x => x.AccuracyMeters)
            .HasColumnName("accuracy_meters");

        builder.Property(x => x.AltitudeMeters)
            .HasColumnName("altitude_meters");

        builder.Property(x => x.SpeedMetersPerSecond)
            .HasColumnName("speed_mps");

        builder.Property(x => x.BearingDegrees)
            .HasColumnName("bearing_degrees");

        builder.Property(x => x.BatteryPercent)
            .HasColumnName("battery_percent");

        builder.Property(x => x.IsCharging)
            .HasColumnName("is_charging");

        builder.Property(x => x.Provider)
            .HasColumnName("provider")
            .HasConversion<short>();

        builder.Property(x => x.RecordedAt)
            .HasColumnName("recorded_at");

        builder.Property(x => x.ReceivedAt)
            .HasColumnName("received_at");

        // Idempotency: a device replaying its offline queue can send the same client id twice.
        builder.HasIndex(x => new { x.DeviceId, x.ClientId })
            .IsUnique()
            .HasDatabaseName("ix_location_records_device_id_client_id");

        // Newest-first history reads and the "current location" lookup ride on this index.
        builder.HasIndex(x => new { x.DeviceId, x.RecordedAt })
            .IsDescending(false, true)
            .HasDatabaseName("ix_location_records_device_id_recorded_at");

        builder.HasOne<ChildDevice>()
            .WithMany()
            .HasForeignKey(x => x.DeviceId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
