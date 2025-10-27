using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ReceiptCollector.Analytics.Infrastructure.Persistence.Postgres.Configurations;

internal sealed class UserAuthLinkConfiguration : IEntityTypeConfiguration<UserAuthLinkEntity>
{
    public void Configure(EntityTypeBuilder<UserAuthLinkEntity> builder)
    {
        builder.ToTable("user_auth_links");
        builder.HasKey(link => link.Id);

        builder.Property(link => link.TokenHash)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(link => link.CreatedAt)
            .IsRequired();

        builder.Property(link => link.ExpiresAt)
            .IsRequired();

        builder.HasIndex(link => link.UserId)
            .IsUnique();

        builder.HasIndex(link => link.TokenHash)
            .IsUnique();
    }
}
