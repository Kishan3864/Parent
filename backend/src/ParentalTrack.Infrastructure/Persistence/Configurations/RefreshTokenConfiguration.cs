using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ParentalTrack.Domain.Entities;

namespace ParentalTrack.Infrastructure.Persistence.Configurations;

/// <summary>Maps <see cref="RefreshToken"/> to <c>refresh_tokens</c>.</summary>
public sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("refresh_tokens");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id");

        builder.Property(x => x.ParentId)
            .HasColumnName("parent_id");

        builder.Property(x => x.TokenHash)
            .HasColumnName("token_hash")
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(x => x.ExpiresAt)
            .HasColumnName("expires_at");

        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at");

        builder.Property(x => x.RevokedAt)
            .HasColumnName("revoked_at");

        builder.HasIndex(x => x.TokenHash)
            .IsUnique()
            .HasDatabaseName("ix_refresh_tokens_token_hash");

        builder.HasIndex(x => x.ParentId)
            .HasDatabaseName("ix_refresh_tokens_parent_id");

        builder.HasOne<Parent>()
            .WithMany()
            .HasForeignKey(x => x.ParentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
