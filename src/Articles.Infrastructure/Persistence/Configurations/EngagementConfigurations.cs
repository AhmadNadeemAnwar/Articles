using Articles.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Articles.Infrastructure.Persistence.Configurations;

public class CommentConfiguration : IEntityTypeConfiguration<Comment>
{
    public void Configure(EntityTypeBuilder<Comment> builder)
    {
        builder.ToTable("comments");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Body).IsRequired().HasMaxLength(Comment.MaxBodyLength);

        builder.HasOne(c => c.Author)
            .WithMany()
            .HasForeignKey(c => c.AuthorId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(c => c.Parent)
            .WithMany(c => c.Replies)
            .HasForeignKey(c => c.ParentId)
            // Replies are removed explicitly with their parent's post, not via a self-referencing cascade.
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(c => new { c.PostId, c.CreatedAt });
    }
}

public class ReactionConfiguration : IEntityTypeConfiguration<Reaction>
{
    public void Configure(EntityTypeBuilder<Reaction> builder)
    {
        builder.ToTable("reactions");
        // One row per visitor, post and kind: the key is what makes a second click an undo, not a second count.
        builder.HasKey(r => new { r.PostId, r.VisitorKey, r.Kind });

        builder.Property(r => r.VisitorKey).IsRequired().HasMaxLength(Reaction.VisitorKeyLength);
        builder.Property(r => r.Kind).HasConversion<int>();

        builder.HasOne(r => r.Post).WithMany().HasForeignKey(r => r.PostId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class PostViewStatConfiguration : IEntityTypeConfiguration<PostViewStat>
{
    public void Configure(EntityTypeBuilder<PostViewStat> builder)
    {
        builder.ToTable("post_view_stats");
        // One row per post, day and combination of facts; a view adds one to its row.
        builder.HasKey(s => new { s.PostId, s.Day, s.Country, s.Device, s.Source, s.Returning });

        builder.Property(s => s.Country).IsRequired().HasMaxLength(PostViewStat.CountryLength);
        builder.Property(s => s.Device).IsRequired().HasMaxLength(10);
        builder.Property(s => s.Source).IsRequired().HasMaxLength(PostViewStat.MaxSourceLength);

        builder.HasOne<Post>().WithMany().HasForeignKey(s => s.PostId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(s => s.Day);
    }
}

public class PostViewConfiguration : IEntityTypeConfiguration<PostView>
{
    public void Configure(EntityTypeBuilder<PostView> builder)
    {
        builder.ToTable("post_views");
        builder.HasKey(v => new { v.PostId, v.VisitorKey, v.Day });

        builder.Property(v => v.VisitorKey).IsRequired().HasMaxLength(Reaction.VisitorKeyLength);

        builder.HasOne<Post>().WithMany().HasForeignKey(v => v.PostId).OnDelete(DeleteBehavior.Cascade);

        // Covers the daily clear-out of older rows.
        builder.HasIndex(v => v.Day);
    }
}

public class BookmarkConfiguration : IEntityTypeConfiguration<Bookmark>
{
    public void Configure(EntityTypeBuilder<Bookmark> builder)
    {
        builder.ToTable("bookmarks");
        builder.HasKey(b => new { b.PostId, b.UserId });

        builder.HasOne(b => b.Post).WithMany().HasForeignKey(b => b.PostId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(b => b.User).WithMany().HasForeignKey(b => b.UserId).OnDelete(DeleteBehavior.Cascade);

        // Reading list, newest saved first.
        builder.HasIndex(b => new { b.UserId, b.CreatedAt });
    }
}

public class UserFollowConfiguration : IEntityTypeConfiguration<UserFollow>
{
    public void Configure(EntityTypeBuilder<UserFollow> builder)
    {
        builder.ToTable("user_follows");
        builder.HasKey(f => new { f.FollowerId, f.FolloweeId });

        builder.HasOne(f => f.Follower)
            .WithMany()
            .HasForeignKey(f => f.FollowerId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(f => f.Followee)
            .WithMany()
            .HasForeignKey(f => f.FolloweeId)
            // Restrict on the second leg: two cascade paths into the same table is not portable.
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(f => f.FolloweeId);
    }
}

public class TagFollowConfiguration : IEntityTypeConfiguration<TagFollow>
{
    public void Configure(EntityTypeBuilder<TagFollow> builder)
    {
        builder.ToTable("tag_follows");
        builder.HasKey(f => new { f.UserId, f.TagId });

        builder.HasOne(f => f.User).WithMany().HasForeignKey(f => f.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(f => f.Tag).WithMany().HasForeignKey(f => f.TagId).OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(f => f.TagId);
    }
}
