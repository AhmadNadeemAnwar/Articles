using Articles.Application.Auth.Dtos;

namespace Articles.Application.Auth;

public interface IAuthService
{
    Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken ct = default);
    Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken ct = default);
    Task<CurrentUserDto> GetCurrentAsync(Guid userId, CancellationToken ct = default);
}
