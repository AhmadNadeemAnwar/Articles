using Articles.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Articles.Infrastructure.Persistence.Configurations;

public class StoredImageConfiguration : IEntityTypeConfiguration<StoredImage>
{
    public void Configure(EntityTypeBuilder<StoredImage> builder)
    {
        builder.ToTable("images");
        builder.HasKey(i => i.Id);

        builder.Property(i => i.ContentType).IsRequired().HasMaxLength(32);
        builder.Property(i => i.Data).IsRequired();

        builder.HasIndex(i => i.OwnerId);
    }
}
