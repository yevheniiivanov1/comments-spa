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
        builder.HasIndex(m => m.Id).HasFilter("[ProcessedAt] IS NULL").HasDatabaseName("IX_OutboxMessages_Pending");
    }
}
