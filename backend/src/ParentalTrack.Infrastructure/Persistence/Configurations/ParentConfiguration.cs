using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ParentalTrack.Domain.Entities;

namespace ParentalTrack.Infrastructure.Persistence.Configurations;

/// <summary>Maps <see cref="Parent"/> to <c>parents</c>.</summary>
public sealed class ParentConfiguration : IEntityTypeConfiguration<Parent>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Parent> builder)
    {
        builder.ToTable("parents");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id");

        builder.Property(x => x.Email)
            .HasColumnName("email")
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(x => x.EmailNormalized)
            .HasColumnName("email_normalized")
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(x => x.PasswordHash)
            .HasColumnName("password_hash")
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(x => x.DisplayName)
            .HasColumnName("display_name")
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(x => x.IsActive)
            .HasColumnName("is_active")
            .HasDefaultValue(true);

        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at");

        builder.HasIndex(x => x.EmailNormalized)
            .IsUnique()
            .HasDatabaseName("ix_parents_email_normalized");
    }
}
