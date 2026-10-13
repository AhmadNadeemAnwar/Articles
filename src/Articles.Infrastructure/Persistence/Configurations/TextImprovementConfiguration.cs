using Articles.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Articles.Infrastructure.Persistence.Configurations;

public class TextImprovementConfiguration : IEntityTypeConfiguration<TextImprovement>
{
    public void Configure(EntityTypeBuilder<TextImprovement> builder)
    {
        builder.ToTable("text_improvements");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.OriginalJson).IsRequired();
        builder.Property(t => t.ImprovedJson).IsRequired();
        builder.Property(t => t.Admin).IsRequired().HasMaxLength(256);

        // Deleting a post takes the originals kept for it with it.
        builder.HasOne(t => t.Post).WithMany().HasForeignKey(t => t.PostId).OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(t => new { t.PostId, t.CreatedAt });
    }
}
