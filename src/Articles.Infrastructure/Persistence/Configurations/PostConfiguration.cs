using Articles.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Articles.Infrastructure.Persistence.Configurations;

public class PostConfiguration : IEntityTypeConfiguration<Post>
{
    public void Configure(EntityTypeBuilder<Post> builder)
    {
        builder.ToTable("posts");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Title).IsRequired().HasMaxLength(Post.MaxTitleLength);
        builder.Property(p => p.Subtitle).HasMaxLength(Post.MaxSubtitleLength);
        builder.Property(p => p.Slug).HasMaxLength(120);
        builder.Property(p => p.CoverImageUrl).HasMaxLength(2048);
        builder.Property(p => p.ContentJson).IsRequired();
        builder.Property(p => p.PlainText).IsRequired();
        builder.Property(p => p.Status).HasConversion<int>();

        // Slugs are unique across published posts; drafts have no slug yet, so nulls are allowed.
        builder.HasIndex(p => p.Slug).IsUnique();

        // Covers the default feed query: published posts, newest first.
        builder.HasIndex(p => new { p.Status, p.PublishedAt });

        // Covers the author's own dashboard listing.
        builder.HasIndex(p => new { p.AuthorId, p.Status, p.UpdatedAt });

        // Deleting a category leaves its posts in place, simply without one.
        builder.HasOne(p => p.Category)
            .WithMany()
            .HasForeignKey(p => p.CategoryId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasMany(p => p.Comments)
            .WithOne(c => c.Post)
            .HasForeignKey(c => c.PostId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(p => p.Revisions)
            .WithOne(r => r.Post)
            .HasForeignKey(r => r.PostId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class PostRevisionConfiguration : IEntityTypeConfiguration<PostRevision>
{
    public void Configure(EntityTypeBuilder<PostRevision> builder)
    {
        builder.ToTable("post_revisions");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.Title).IsRequired().HasMaxLength(Post.MaxTitleLength);
        builder.Property(r => r.ContentJson).IsRequired();

        builder.HasIndex(r => new { r.PostId, r.CreatedAt });
    }
}
