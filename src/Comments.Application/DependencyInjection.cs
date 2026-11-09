using Comments.Application.Comments;
using Microsoft.Extensions.DependencyInjection;

namespace Comments.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<CommentQueries>();
        services.AddScoped<PostCommentHandler>();
        return services;
    }
}
