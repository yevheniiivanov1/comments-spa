using Comments.Domain.Comments;

namespace Comments.Application.Comments;

public sealed record CommentDto(
    long Id,
    long? ParentId,
    long? RootId,
    int Depth,
    string UserName,
    string Email,
    string? HomePage,
    string Html,
    DateTimeOffset CreatedAt,
    int ReplyCount,
    AttachmentDto? Attachment)
{
    public static CommentDto From(Comment comment) => new(
        comment.Id,
        comment.ParentId,
        comment.RootId,
        comment.Depth,
        comment.UserName,
        comment.Email,
        comment.HomePage,
        comment.Html,
        comment.CreatedAt,
        comment.ReplyCount,
        comment.Attachment is { } attachment ? AttachmentDto.From(attachment) : null);
}

public sealed record AttachmentDto(AttachmentKind Kind, string Url, string FileName, string ContentType, long SizeBytes, int? Width, int? Height)
{
    public const string UrlPrefix = "/files/";

    public static AttachmentDto From(Attachment attachment)
    {
        var image = attachment as ImageAttachment;
        return new AttachmentDto(
            attachment.Kind,
            UrlPrefix + attachment.StorageKey,
            attachment.FileName,
            attachment.ContentType,
            attachment.SizeBytes,
            image?.Width,
            image?.Height);
    }
}

public sealed record CommentPage(IReadOnlyList<CommentDto> Items, int Page, int PageSize, int TotalCount, CommentSort Sort, SortDirection Direction)
{
    public int TotalPages => TotalCount == 0 ? 1 : (TotalCount + PageSize - 1) / PageSize;
}

/// <summary>Replies of one thread in id order, which puts every parent before its children. <see cref="NextAfter"/> is the cursor for the next page.</summary>
public sealed record ThreadPage(long RootId, IReadOnlyList<CommentDto> Items, long? NextAfter);

public enum CommentSort
{
    CreatedAt,
    UserName,
    Email,
}

public enum SortDirection
{
    Desc,
    Asc,
}

/// <summary>Published through the outbox after a comment is committed; drives search indexing and live updates.</summary>
public sealed record CommentPostedMessage(CommentDto Comment)
{
    public const string RoutingKey = "comment.posted";
}
