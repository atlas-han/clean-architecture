using CleanArchitecture.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CleanArchitecture.Infrastructure.Persistence.Configurations
{
    public class OrderConfiguration : IEntityTypeConfiguration<Order>
    {
        public void Configure(EntityTypeBuilder<Order> builder)
        {
            builder.HasKey(o => o.Id);

            builder.Property(o => o.CustomerName)
                .IsRequired()
                .HasMaxLength(200);

            builder.Property(o => o.Status)
                .IsRequired()
                .HasConversion<int>();

            builder.Ignore(o => o.TotalAmount);

            builder.HasMany(o => o.Items)
                .WithOne()
                .HasForeignKey(i => i.OrderId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Navigation(o => o.Items)
                .UsePropertyAccessMode(PropertyAccessMode.Field);
        }
    }
}
