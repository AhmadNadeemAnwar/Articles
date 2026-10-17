using Articles.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Articles.Infrastructure.Persistence.Configurations;

public class ReaderFeedbackConfiguration : IEntityTypeConfiguration<ReaderFeedback>
{
    public void Configure(EntityTypeBuilder<ReaderFeedback> builder)
    {
        builder.ToTable("reader_feedback");
        builder.HasKey(f => f.Id);

        builder.Property(f => f.Liked).IsRequired().HasMaxLength(100);
        builder.Property(f => f.Text).IsRequired().HasMaxLength(ReaderFeedback.MaxTextLength);
        builder.Ignore(f => f.LikedList);

        // Deleting a post takes the feedback about it with it.
        builder.HasOne(f => f.Post).WithMany().HasForeignKey(f => f.PostId).OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(f => f.CreatedAt);
    }
}
