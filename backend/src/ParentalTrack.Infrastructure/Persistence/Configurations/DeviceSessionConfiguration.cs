using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ParentalTrack.Domain.Entities;

namespace ParentalTrack.Infrastructure.Persistence.Configurations;

/// <summary>Maps <see cref="DeviceSession"/> to <c>device_sessions</c>.</summary>
public sealed class DeviceSessionConfiguration : IEntityTypeConfiguration<DeviceSession>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<DeviceSession> builder)
    {
        builder.ToTable("device_sessions");

        builder.HasKey(x => x.Id);

        // The id doubles as the JWT "jti"; it is assigned in application code, never by the database.
        builder.Property(x => x.Id)
            .HasColumnName("id");

        builder.Property(x => x.DeviceId)
            .HasColumnName("device_id");

        builder.Property(x => x.IssuedAt)
            .HasColumnName("issued_at");

        builder.Property(x => x.ExpiresAt)
            .HasColumnName("expires_at");

        builder.Property(x => x.RevokedAt)
            .HasColumnName("revoked_at");

        builder.Property(x => x.RevokedReason)
            .HasColumnName("revoked_reason")
            .HasMaxLength(128);

        builder.Property(x => x.EnrolledUserAgent)
            .HasColumnName("enrolled_user_agent")
            .HasMaxLength(256);

        builder.HasIndex(x => x.DeviceId)
            .HasDatabaseName("ix_device_sessions_device_id");

        builder.HasOne<ChildDevice>()
            .WithMany()
            .HasForeignKey(x => x.DeviceId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
