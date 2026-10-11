using System.Text;
using Articles.Api.Common;
using Articles.Api.Extensions;
using Articles.Api.Filters;
using Articles.Api.Middleware;
using Articles.Application;
using Articles.Application.Common;
using Articles.Infrastructure;
using Articles.Infrastructure.Persistence;
using Articles.Infrastructure.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.IdentityModel.Tokens;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Hosts such as Render tell the app which port to listen on through PORT.
var hostedPort = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrEmpty(hostedPort)) builder.WebHost.UseUrls($"http://0.0.0.0:{hostedPort}");

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    // The proxy's address is not known ahead of time on a managed host.
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

const string CorsPolicy = "web-client";

builder.Host.UseSerilog((context, configuration) =>
    configuration.ReadFrom.Configuration(context.Configuration));

builder.Services.AddControllers(options => options.Filters.Add<ValidationFilter>());
builder.Services.AddSwaggerWithBearer();

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.Configure<AccountOptions>(builder.Configuration.GetSection(AccountOptions.SectionName));

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();

var jwt = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();

if (string.IsNullOrWhiteSpace(jwt.Key) || jwt.Key.Length < 32)
{
    // Fail loudly at startup rather than issuing tokens signed with a weak or empty key.
    throw new InvalidOperationException(
        "Jwt:Key must be set to at least 32 characters. Set it via configuration or the JWT__KEY environment variable.");
}

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)),
            ClockSkew = TimeSpan.FromMinutes(1)
        };
    });

builder.Services.AddSingleton<IAuthorizationHandler, AdminAuthorizationHandler>();
builder.Services.AddAuthorizationBuilder()
    .AddPolicy(AdminRequirement.PolicyName, policy => policy
        .RequireAuthenticatedUser()
        .AddRequirements(new AdminRequirement()));

// Per-client, per-endpoint-kind limits: tight on sign-in and sign-up, looser on reads.
builder.Services.AddArticlesRateLimiting(builder.Configuration);

builder.Services.AddCors(options => options.AddPolicy(CorsPolicy, policy => policy
    .WithOrigins(builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? ["http://localhost:5173"])
    .AllowAnyHeader()
    .AllowAnyMethod()));

builder.Services.AddHealthChecks().AddDbContextCheck<AppDbContext>();

var app = builder.Build();

// Behind a reverse proxy the socket peer is the proxy, so the original scheme arrives in
// X-Forwarded-Proto. (The client address used for rate limiting is resolved separately, from the
// platform's trusted header; see ClientIp.)
app.UseForwardedHeaders();

app.UseMiddleware<SecurityHeadersMiddleware>(app.Environment.IsDevelopment());
if (!app.Environment.IsDevelopment()) app.UseHsts();

// Registered first so it wraps everything downstream, including routing failures.
app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseSerilogRequestLogging();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options => options.SwaggerEndpoint("/swagger/v1/swagger.json", "Articles API v1"));
}

app.UseCors(CorsPolicy);
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health");

// Apply migrations and seed on boot so a fresh clone has a working, populated database.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
    await DbInitializer.InitialiseAsync(db, hasher, app.Configuration.GetValue<bool>("Seed:Enabled"));
}

app.Run();

// Exposes the entry point to the integration-test host.
public partial class Program;
