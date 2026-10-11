using Articles.Domain.Exceptions;

namespace Articles.Domain.Entities;

/// <summary>Reader-to-writer subscription. Drives the personalised feed.</summary>
public class UserFollow
{
    public Guid FollowerId { get; private set; }
    public User Follower { get; private set; } = null!;
    public Guid FolloweeId { get; private set; }
    public User Followee { get; private set; } = null!;
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;

    private UserFollow() { }

    public UserFollow(Guid followerId, Guid followeeId)
    {
        if (followerId == followeeId) throw new DomainException("You cannot follow yourself.");
        FollowerId = followerId;
        FolloweeId = followeeId;
    }
}
