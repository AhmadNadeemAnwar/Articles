using Articles.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Articles.Infrastructure.Persistence.Configurations;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");
        builder.HasKey(u => u.Id);

        builder.Property(u => u.Email).IsRequired().HasMaxLength(254);
        builder.Property(u => u.Handle).IsRequired().HasMaxLength(30);
        builder.Property(u => u.DisplayName).IsRequired().HasMaxLength(60);
        builder.Property(u => u.PasswordHash).IsRequired().HasMaxLength(200);
        builder.Property(u => u.Bio).HasMaxLength(300);
        builder.Property(u => u.AvatarUrl).HasMaxLength(2048);
        builder.Property(u => u.WebsiteUrl).HasMaxLength(2048);

        builder.HasIndex(u => u.Email).IsUnique();
        builder.HasIndex(u => u.Handle).IsUnique();

        builder.HasMany(u => u.Posts)
            .WithOne(p => p.Author)
            .HasForeignKey(p => p.AuthorId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
