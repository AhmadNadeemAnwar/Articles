using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Articles.Api.Common;

public static class RateLimiting
{
    public static IServiceCollection AddArticlesRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        var trustedHeaders = configuration.GetSection(ClientIp.HeadersConfigKey).Get<string[]>() ?? [];

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(
                context => Partition(context, ClientIp.Resolve(context, trustedHeaders)));
            options.OnRejected = WriteRejection;
        });

        return services;
    }

    /// <summary>
    /// One limiter per client address per kind of request, so a burst of searches cannot lock someone
    /// out of signing in, and a flood of sign-in attempts cannot starve reading.
    /// </summary>
    internal static RateLimitPartition<string> Partition(HttpContext context, string ip)
    {
        var path = (context.Request.Path.Value ?? string.Empty).TrimEnd('/');
        var method = context.Request.Method;

        // Hosting platforms poll this from inside their network; it must never be throttled.
        if (path.Equals("/health", StringComparison.OrdinalIgnoreCase))
            return RateLimitPartition.GetNoLimiter("health");

        if (HttpMethods.IsPost(method) && path.Equals("/api/v1/auth/login", StringComparison.OrdinalIgnoreCase))
            return Fixed($"login:{ip}", permits: 10, TimeSpan.FromMinutes(1));

        // Admin sign-in guards the most powerful account on the site, so it gets the tightest limit of all.
        if (HttpMethods.IsPost(method) && path.Equals("/api/v1/admin/auth/login", StringComparison.OrdinalIgnoreCase))
            return Fixed($"adminlogin:{ip}", permits: 5, TimeSpan.FromMinutes(1));

        // Accounts are cheap to mint and are the raw material for spam, so sign-ups get the tightest limit.
        if (HttpMethods.IsPost(method) && path.Equals("/api/v1/auth/register", StringComparison.OrdinalIgnoreCase))
            return Fixed($"register:{ip}", permits: 5, TimeSpan.FromHours(1));

        // Each request here can send an email to an address the caller chose, so it is held far below ordinary writes.
        if (HttpMethods.IsPost(method) && path.Equals("/api/v1/subscribers", StringComparison.OrdinalIgnoreCase))
            return Fixed($"subscribe:{ip}", permits: 5, TimeSpan.FromHours(1));

        // Feedback is anonymous, so each reader is held to a handful an hour; enough for honest use, too few to flood the list.
        if (HttpMethods.IsPost(method) && path.StartsWith("/api/v1/posts/", StringComparison.OrdinalIgnoreCase) && path.EndsWith("/feedback", StringComparison.OrdinalIgnoreCase))
            return Fixed($"feedback:{ip}", permits: 5, TimeSpan.FromHours(1));

        // The free grammar service allows about 20 checks a minute; staying under it keeps the whole site from being blocked.
        if (HttpMethods.IsPost(method) && path.Equals("/api/v1/admin/grammar/check", StringComparison.OrdinalIgnoreCase))
            return Fixed($"grammar:{ip}", permits: 15, TimeSpan.FromMinutes(1));

        if (HttpMethods.IsGet(method) || HttpMethods.IsHead(method))
        {
            // Search scans post bodies, so it is far more expensive than fetching a feed.
            if (context.Request.Query.ContainsKey("q"))
                return Fixed($"search:{ip}", permits: 30, TimeSpan.FromMinutes(1));

            return Fixed($"read:{ip}", permits: 300, TimeSpan.FromMinutes(1));
        }

        return Fixed($"write:{ip}", permits: 30, TimeSpan.FromMinutes(1));
    }

    private static RateLimitPartition<string> Fixed(string key, int permits, TimeSpan window) =>
        RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permits,
            Window = window,
            QueueLimit = 0,
            AutoReplenishment = true
        });

    private static async ValueTask WriteRejection(OnRejectedContext context, CancellationToken ct)
    {
        var response = context.HttpContext.Response;

        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
            response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString();

        response.StatusCode = StatusCodes.Status429TooManyRequests;
        response.ContentType = "application/problem+json";

        await response.WriteAsync(JsonSerializer.Serialize(new ProblemDetails
        {
            Status = StatusCodes.Status429TooManyRequests,
            Title = "Too many requests",
            Detail = "You're going too fast. Wait a moment and try again.",
            Instance = context.HttpContext.Request.Path
        }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }), ct);
    }
}
