namespace Articles.Api.Middleware;

/// <summary>
/// Defensive response headers for the JSON API. The API never serves HTML in production, so the
/// policy is as strict as it can be: nothing may be loaded or framed from an API response, and
/// authenticated responses are never cached by browsers or intermediaries.
/// </summary>
public sealed class SecurityHeadersMiddleware
{
    /// <summary>Set on a request by the endpoint that serves stored pictures, which are public and never change.</summary>
    public const string PublicImageKey = "Articles.PublicImage";

    private readonly RequestDelegate _next;
    private readonly bool _allowSwaggerUi;

    public SecurityHeadersMiddleware(RequestDelegate next, bool allowSwaggerUi)
    {
        _next = next;
        _allowSwaggerUi = allowSwaggerUi;
    }

    public Task InvokeAsync(HttpContext context)
    {
        // Set when the response starts so the headers also land on error and rate-limit responses.
        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;
            headers["X-Content-Type-Options"] = "nosniff";
            headers["Referrer-Policy"] = "no-referrer";
            headers["X-Frame-Options"] = "DENY";

            var isSwagger = _allowSwaggerUi && context.Request.Path.StartsWithSegments("/swagger");
            if (context.Items.ContainsKey(PublicImageKey) && context.Response.StatusCode == StatusCodes.Status200OK)
            {
                // A picture may be shown by the sites that embed it and kept by browsers; it can still
                // load nothing itself. The endpoint has already set its own Cache-Control.
                headers["Content-Security-Policy"] = "default-src 'none'; img-src 'self'; style-src 'unsafe-inline'; frame-ancestors 'none'";
                headers["Cross-Origin-Resource-Policy"] = "cross-origin";
            }
            else if (!isSwagger)
            {
                headers["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'";
                headers["Cache-Control"] = "no-store";
            }

            return Task.CompletedTask;
        });

        return _next(context);
    }
}
