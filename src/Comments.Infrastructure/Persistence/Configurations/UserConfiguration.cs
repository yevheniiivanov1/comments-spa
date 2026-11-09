using Comments.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Comments.Infrastructure.Persistence.Configurations;

/// <summary>Users table. The e-mail index is unique under the database's case-insensitive collation.</summary>
internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("Users");
        builder.HasKey(u => u.Id);
        builder.Property(u => u.UserName).HasMaxLength(UserName.MaxLength).IsUnicode(false);
        builder.Property(u => u.Email).HasMaxLength(EmailAddress.MaxLength).IsUnicode(false);
        builder.Property(u => u.HomePage).HasMaxLength(HomePage.MaxLength);
        builder.Property(u => u.CreatedAt).HasPrecision(3);
        builder.Property(u => u.LastSeenAt).HasPrecision(3);
        builder.HasIndex(u => u.Email).IsUnique();
    }
}
