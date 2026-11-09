using Comments.Domain.Comments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Comments.Infrastructure.Persistence.Configurations;

/// <summary>Attachments: one table for the class hierarchy (TPH), told apart by the <c>Type</c> column.</summary>
internal sealed class AttachmentConfiguration : IEntityTypeConfiguration<Attachment>
{
    public void Configure(EntityTypeBuilder<Attachment> builder)
    {
        builder.ToTable("Attachments");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();

        builder.HasDiscriminator<string>("Type")
            .HasValue<ImageAttachment>("image")
            .HasValue<TextAttachment>("text");
        builder.Property<string>("Type").HasMaxLength(10).IsUnicode(false);

        builder.Property(a => a.FileName).HasMaxLength(Attachment.MaxFileNameLength);
        builder.Property(a => a.StorageKey).HasMaxLength(100).IsUnicode(false);
        builder.Property(a => a.ContentType).HasMaxLength(100).IsUnicode(false);
        builder.Ignore(a => a.Kind);

        builder.HasIndex(a => a.CommentId).IsUnique();
        builder.HasIndex(a => a.StorageKey).IsUnique();
    }
}

internal sealed class ImageAttachmentConfiguration : IEntityTypeConfiguration<ImageAttachment>
{
    public void Configure(EntityTypeBuilder<ImageAttachment> builder)
    {
        builder.Property(a => a.Width);
        builder.Property(a => a.Height);
        builder.Property(a => a.WasResized);
    }
}
