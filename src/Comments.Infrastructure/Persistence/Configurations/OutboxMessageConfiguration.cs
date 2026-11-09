using Comments.Infrastructure.Persistence.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Comments.Infrastructure.Persistence.Configurations;

internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("OutboxMessages");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Type).HasMaxLength(100).IsUnicode(false);
        builder.Property(m => m.OccurredAt).HasPrecision(3);
        builder.Property(m => m.ProcessedAt).HasPrecision(3);
        builder.Property(m => m.LastError).HasMaxLength(OutboxMessage.MaxErrorLength);
        builder.Property(m => m.NextAttemptAt).HasPrecision(3);
        builder.Property(m => m.ParkedAt).HasPrecision(3);

        // Filter columns are included so SQL Server can use the filtered indexes (see CommentConfiguration).
        builder.HasIndex(m => m.Id)
            .HasFilter("[ProcessedAt] IS NULL AND [ParkedAt] IS NULL")
            .IncludeProperties(m => new { m.ProcessedAt, m.ParkedAt, m.NextAttemptAt })
            .HasDatabaseName("IX_OutboxMessages_Pending");
        builder.HasIndex(m => m.ProcessedAt)
            .HasFilter("[ProcessedAt] IS NOT NULL")
            .HasDatabaseName("IX_OutboxMessages_Processed");
    }
}
