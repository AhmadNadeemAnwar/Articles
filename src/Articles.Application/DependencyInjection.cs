using FluentValidation;
using Articles.Application.Admin;
using Articles.Application.Auth;
using Articles.Application.Comments;
using Articles.Application.Engagement;
using Articles.Application.Portfolio;
using Articles.Application.Images;
using Articles.Application.Posts;
using Articles.Application.Site;
using Articles.Application.Subscriptions;
using Articles.Application.Tags;
using Articles.Application.Users;
using Microsoft.Extensions.DependencyInjection;

namespace Articles.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IPostService, PostService>();
        services.AddScoped<ICommentService, CommentService>();
        services.AddScoped<IEngagementService, EngagementService>();
        services.AddScoped<IReactionService, ReactionService>();
        services.AddScoped<ITagService, TagService>();
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<IAdminService, AdminService>();
        services.AddScoped<IAdminAuthService, AdminAuthService>();
        services.AddScoped<IPortfolioService, PortfolioService>();
        services.AddScoped<IImageService, ImageService>();
        services.AddScoped<IImageGenerationService, ImageGenerationService>();
        services.AddScoped<ISiteService, SiteService>();
        services.AddScoped<ISubscriptionService, SubscriptionService>();

        services.AddValidatorsFromAssemblyContaining<AuthService>();

        return services;
    }
}
