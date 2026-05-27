using System.Threading;
using System.Threading.Tasks;
using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CleanArchitecture.Application.UnitTests.TestDoubles
{
    public class TestDbContext : DbContext, IApplicationDbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }

        public DbSet<Product> Products => Set<Product>();
        public DbSet<Order> Orders => Set<Order>();

        public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken)
        {
            return Database.BeginTransactionAsync(cancellationToken);
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Order>(b =>
            {
                b.HasKey(o => o.Id);
                b.Ignore(o => o.TotalAmount);
                b.HasMany(o => o.Items)
                    .WithOne()
                    .HasForeignKey(i => i.OrderId)
                    .OnDelete(DeleteBehavior.Cascade);
                b.Navigation(o => o.Items)
                    .UsePropertyAccessMode(PropertyAccessMode.Field);
            });

            modelBuilder.Entity<OrderItem>(b =>
            {
                b.HasKey(i => i.Id);
                b.Ignore(i => i.LineTotal);
            });

            base.OnModelCreating(modelBuilder);
        }
    }
}
