namespace Articles.Application.Auth.Dtos;

public sealed record RegisterRequest(string Email, string Handle, string DisplayName, string Password, string? TurnstileToken = null);

public sealed record LoginRequest(string Email, string Password);

public sealed record CurrentUserDto(
    Guid Id,
    string Email,
    string Handle,
    string DisplayName,
    string? Bio,
    string? AvatarUrl,
    string? WebsiteUrl);

public sealed record AuthResponse(string Token, DateTimeOffset ExpiresAt, CurrentUserDto User);
