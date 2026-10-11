using Articles.Application.Auth.Dtos;
using Articles.Application.Users.Dtos;

namespace Articles.Application.Users;

public interface IUserService
{
    Task<ProfileDto> GetProfileAsync(string handle, Guid? viewerId, CancellationToken ct = default);
    Task<CurrentUserDto> UpdateProfileAsync(Guid userId, UpdateProfileRequest request, CancellationToken ct = default);
}
