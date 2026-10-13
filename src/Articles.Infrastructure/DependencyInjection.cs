using Articles.Application.Subscriptions;
using Articles.Application.Grammar;
using Articles.Infrastructure.Email;
using Articles.Application.Images;
using Articles.Infrastructure.Grammar;
using Articles.Infrastructure.Images;
using Articles.Application.Admin;
using Articles.Application.Common;
using Articles.Application.Portfolio;
using Articles.Infrastructure.Portfolio;
using Articles.Domain.Interfaces;
using Articles.Infrastructure.Persistence;
using Articles.Infrastructure.Persistence.Repositories;
using Articles.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Articles.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var provider = DatabaseOptions.ParseProvider(configuration["Database:Provider"]);
        var connectionString = configuration.GetConnectionString("Default") ?? "Data Source=articles.db";

        services.AddDbContext<AppDbContext>(options =>
        {
            if (provider == DatabaseProvider.Postgres)
            {
                options.UseNpgsql(
                    DatabaseOptions.NormalisePostgresConnectionString(connectionString),
                    npgsql => npgsql.MigrationsAssembly(DatabaseOptions.PostgresMigrationsAssembly));
            }
            else
            {
                options.UseSqlite(connectionString);
            }
        });
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<AppDbContext>());

        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IPostRepository, PostRepository>();
        services.AddScoped<ITagRepository, TagRepository>();
        services.AddScoped<ICommentRepository, CommentRepository>();
        services.AddScoped<IEngagementRepository, EngagementRepository>();
        services.AddScoped<IAdminRepository, AdminRepository>();
        services.AddScoped<IImageRepository, ImageRepository>();
        services.AddScoped<ITextImprovementRepository, TextImprovementRepository>();
        services.AddScoped<IReactionRepository, ReactionRepository>();
        services.AddScoped<IActivityLog, ActivityLog>();
        services.AddScoped<ISiteRepository, SiteRepository>();
        services.AddScoped<ISubscriberRepository, SubscriberRepository>();

        services.Configure<EmailOptions>(configuration.GetSection(EmailOptions.SectionName));
        services.AddHttpClient<IEmailSender, BrevoEmailSender>();
        services.AddSingleton<ISubscriberTokens, SubscriberTokens>();

        services.Configure<GrammarOptions>(configuration.GetSection(GrammarOptions.SectionName));
        services.AddHttpClient<IGrammarChecker, LanguageToolChecker>();

        services.Configure<ImageGenerationOptions>(configuration.GetSection(ImageGenerationOptions.SectionName));
        services.AddHttpClient<IImageGenerator, CloudflareImageGenerator>();
        services.AddHttpClient<Articles.Application.Site.IThemeAi, CloudflareThemeAi>();
        services.AddHttpClient<Articles.Application.Posts.ITextModel, CloudflareThemeAi>();

        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));
        services.AddSingleton<IPasswordHasher, BCryptPasswordHasher>();
        services.AddSingleton<ITokenService, JwtTokenService>();

        services.AddSingleton<ILoginAttemptTracker, InMemoryLoginAttemptTracker>();
        services.AddSingleton<IAdminLoginAttemptTracker, AdminLoginAttemptTracker>();

        services.Configure<PortfolioOptions>(configuration.GetSection(PortfolioOptions.SectionName));
        services.AddHttpClient<IPortfolioContentStore, GitHubPortfolioContentStore>();

        services.Configure<AdminOptions>(configuration.GetSection(AdminOptions.SectionName));
        services.AddSingleton<IAdminDirectory, ConfiguredAdminDirectory>();
        // Singleton: it remembers the last spent code, which must survive across requests.
        services.AddSingleton<ITotpVerifier, TotpVerifier>();

        services.Configure<TurnstileOptions>(configuration.GetSection(TurnstileOptions.SectionName));
        services.AddHttpClient<ITurnstileVerifier, TurnstileVerifier>();

        // On by default; switch off (Security:CheckPwnedPasswords=false) for offline development.
        if (configuration.GetValue("Security:CheckPwnedPasswords", true))
            services.AddHttpClient<IPwnedPasswordChecker, PwnedPasswordChecker>();
        else
            services.AddSingleton<IPwnedPasswordChecker, DisabledPwnedPasswordChecker>();

        return services;
    }
}
