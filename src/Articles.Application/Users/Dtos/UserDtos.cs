namespace Articles.Application.Users.Dtos;

public sealed record ProfileDto(
    Guid Id,
    string Handle,
    string DisplayName,
    string? Bio,
    string? AvatarUrl,
    string? WebsiteUrl,
    DateTimeOffset JoinedAt,
    int PostCount,
    int FollowerCount,
    int FollowingCount,
    bool IsFollowing,
    bool IsSelf);

public sealed record UpdateProfileRequest(string DisplayName, string? Bio, string? AvatarUrl, string? WebsiteUrl);
