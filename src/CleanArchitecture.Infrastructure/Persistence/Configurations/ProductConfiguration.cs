using CleanArchitecture.Domain.Entities;
using CleanArchitecture.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CleanArchitecture.Infrastructure.Persistence.Configurations
{
    public class ProductConfiguration : IEntityTypeConfiguration<Product>
    {
        public void Configure(EntityTypeBuilder<Product> builder)
        {
            builder.HasKey(p => p.Id);

            builder.Property(p => p.Name)
                .IsRequired()
                .HasMaxLength(200);

            builder.Property(p => p.Description)
                .HasMaxLength(2000);

            builder.Property(p => p.Price)
                .HasConversion(money => money.Amount, value => new Money(value))
                .HasPrecision(18, 2);

            // Concurrency token: two PlaceOrders racing on the same product must
            // not both pass DecreaseStock and oversell — the loser's SaveChanges
            // throws DbUpdateConcurrencyException instead of silently overwriting.
            builder.Property(p => p.Stock)
                .IsConcurrencyToken();
        }
    }
}
