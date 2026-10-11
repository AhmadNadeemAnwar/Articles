using System.Text.Json;
using Articles.Domain.Exceptions;
using Microsoft.AspNetCore.Mvc;

namespace Articles.Api.Middleware;

/// <summary>
/// Translates domain exceptions into RFC 7807 problem details, so controllers never
/// have to think about status codes for expected failure modes.
/// </summary>
public sealed class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;
    private readonly IHostEnvironment _environment;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger, IHostEnvironment environment)
    {
        _next = next;
        _logger = logger;
        _environment = environment;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            await HandleAsync(context, ex);
        }
    }

    private async Task HandleAsync(HttpContext context, Exception exception)
    {
        var (status, title) = exception switch
        {
            NotFoundException => (StatusCodes.Status404NotFound, "Not found"),
            ForbiddenException => (StatusCodes.Status403Forbidden, "Forbidden"),
            ConflictException => (StatusCodes.Status409Conflict, "Conflict"),
            TooManyRequestsException => (StatusCodes.Status429TooManyRequests, "Too many requests"),
            ServiceUnavailableException => (StatusCodes.Status503ServiceUnavailable, "Not available"),
            ExternalServiceException => (StatusCodes.Status502BadGateway, "Upstream service problem"),
            DomainException => (StatusCodes.Status400BadRequest, "Invalid request"),
            _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred")
        };

        if (status == StatusCodes.Status500InternalServerError)
        {
            _logger.LogError(exception, "Unhandled exception on {Method} {Path}", context.Request.Method, context.Request.Path);
        }
        else
        {
            _logger.LogInformation("{Title} on {Method} {Path}: {Message}",
                title, context.Request.Method, context.Request.Path, exception.Message);
        }

        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            // Internal errors must not leak stack traces or messages to clients in production.
            Detail = status == StatusCodes.Status500InternalServerError && !_environment.IsDevelopment()
                ? "Something went wrong. Please try again."
                : exception.Message,
            Instance = context.Request.Path
        };

        context.Response.Clear();
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/problem+json";

        await context.Response.WriteAsync(JsonSerializer.Serialize(problem, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        }));
    }
}
