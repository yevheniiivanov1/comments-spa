using Microsoft.AspNetCore.SignalR;

namespace Comments.Api.Realtime;

/// <summary>
/// WebSocket hub for live updates. Clients only listen: the server sends <c>commentPosted</c> with the new comment.
/// </summary>
public sealed class CommentsHub : Hub
{
    public const string Path = "/hubs/comments";
    public const string CommentPosted = "commentPosted";
}
