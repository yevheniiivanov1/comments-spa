using Comments.Application.Comments;
using Comments.Infrastructure.Messaging;
using Microsoft.AspNetCore.SignalR;
using RabbitMQ.Client;

namespace Comments.Api.Realtime;

/// <summary>
/// Forwards comment events from RabbitMQ to this instance's WebSocket clients. Every API instance has its own
/// exclusive queue on the events exchange, so the broker acts as the SignalR backplane: a comment posted through
/// any instance reaches the readers connected to all of them. Delivery is best effort; missed updates show up on reload.
/// </summary>
internal sealed class LiveUpdatesConsumer(IServiceProvider services, IHubContext<CommentsHub> hub, ILogger<LiveUpdatesConsumer> logger)
    : RabbitMqConsumer<CommentPostedMessage>(services, logger)
{
    protected override bool RequeueOnFailure => false;

    protected override Task<string> DeclareQueueAsync(IChannel channel, CancellationToken cancellationToken) =>
        RabbitMqTopology.DeclareInstanceQueueAsync(channel, CommentPostedMessage.RoutingKey, cancellationToken);

    protected override Task HandleAsync(CommentPostedMessage message, CancellationToken cancellationToken) =>
        hub.Clients.All.SendAsync(CommentsHub.CommentPosted, message.Comment, cancellationToken);
}
