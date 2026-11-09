using Comments.Domain.Comments;
using Comments.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Comments.Infrastructure.Persistence.Configurations;

/// <summary>
/// Comments table. The filtered indexes back the three sorts of the top-level list and the thread read;
/// <c>(key, Id)</c> makes each ORDER BY a plain index scan in either direction.
/// <para>
/// The top-level indexes INCLUDE <c>ParentId</c> although it is always NULL in them: without it SQL Server re-checks
/// <c>ParentId IS NULL</c> with a key lookup for every row an OFFSET skips, so the optimizer scans and sorts the whole table
/// instead (measured on 1M rows: 7.4 s cold for the last page; with the INCLUDE it is an ordered index scan).
/// </para>
/// </summary>
internal sealed class CommentConfiguration : IEntityTypeConfiguration<Comment>
{
    public const string TopLevelFilter = "[ParentId] IS NULL";

    public void Configure(EntityTypeBuilder<Comment> builder)
    {
        builder.ToTable("Comments", t => t.HasCheckConstraint(
            "CK_Comments_Tree",
            "([ParentId] IS NULL AND [RootId] IS NULL AND [Depth] = 0) OR ([ParentId] IS NOT NULL AND [RootId] IS NOT NULL AND [Depth] > 0)"));

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).UseIdentityColumn();

        builder.HasOne<Comment>().WithMany().HasForeignKey(c => c.ParentId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Comment>().WithMany().HasForeignKey(c => c.RootId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(c => c.User).WithMany().HasForeignKey(c => c.UserId).OnDelete(DeleteBehavior.NoAction);

        builder.Property(c => c.UserName).HasMaxLength(UserName.MaxLength).IsUnicode(false);
        builder.Property(c => c.Email).HasMaxLength(EmailAddress.MaxLength).IsUnicode(false);
        builder.Property(c => c.HomePage).HasMaxLength(HomePage.MaxLength);
        builder.Property(c => c.Html);
        builder.Property(c => c.CreatedAt).HasPrecision(3);
        builder.Property(c => c.ReplyCount).HasDefaultValue(0);

        builder.ComplexProperty(c => c.Client, client =>
        {
            client.Property(x => x.IpAddress).HasColumnName("IpAddress").HasMaxLength(ClientInfo.MaxIpAddressLength).IsUnicode(false);
            client.Property(x => x.UserAgent).HasColumnName("UserAgent").HasMaxLength(ClientInfo.MaxUserAgentLength);
        });

        builder.HasOne(c => c.Attachment).WithOne().HasForeignKey<Attachment>(a => a.CommentId).OnDelete(DeleteBehavior.Cascade);

        builder.Ignore(c => c.IsTopLevel);

        builder.HasIndex(c => new { c.CreatedAt, c.Id }).HasFilter(TopLevelFilter).IncludeProperties(c => c.ParentId).HasDatabaseName("IX_Comments_TopLevel_CreatedAt");
        builder.HasIndex(c => new { c.UserName, c.Id }).HasFilter(TopLevelFilter).IncludeProperties(c => c.ParentId).HasDatabaseName("IX_Comments_TopLevel_UserName");
        builder.HasIndex(c => new { c.Email, c.Id }).HasFilter(TopLevelFilter).IncludeProperties(c => c.ParentId).HasDatabaseName("IX_Comments_TopLevel_Email");
        builder.HasIndex(c => new { c.RootId, c.Id }).HasFilter("[RootId] IS NOT NULL").HasDatabaseName("IX_Comments_RootId_Id");
        builder.HasIndex(c => c.ParentId).HasFilter("[ParentId] IS NOT NULL");
    }
}
