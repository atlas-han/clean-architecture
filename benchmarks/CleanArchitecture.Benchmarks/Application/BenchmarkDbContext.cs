using System;
using System.Threading;
using System.Threading.Tasks;
using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Domain.Common;
using CleanArchitecture.Domain.Entities;
using CleanArchitecture.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CleanArchitecture.Benchmarks.Application
{
    // In-memory IApplicationDbContext used to drive Application handlers from the
    // benchmark harness without a real database. Mirrors the unit-test double's model
    // config + audit-field stamping so handlers run exactly as they do in production.
    public class BenchmarkDbContext : DbContext, IApplicationDbContext
    {
        private DateTime _utcNow = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        public BenchmarkDbContext(DbContextOptions<BenchmarkDbContext> options) : base(options) { }

        public DbSet<Product> Products => Set<Product>();
        public DbSet<Order> Orders => Set<Order>();

        // Each call gets an isolated InMemory store (fresh GUID name).
        public static BenchmarkDbContext CreateInMemory()
        {
            var options = new DbContextOptionsBuilder<BenchmarkDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options;
            return new BenchmarkDbContext(options);
        }

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            ApplyAuditFields();
            return base.SaveChangesAsync(cancellationToken);
        }

        public async Task<TResult> ExecuteInTransactionAsync<TResult>(
            Func<CancellationToken, Task<TResult>> operation,
            CancellationToken cancellationToken)
        {
            await using var transaction = await Database.BeginTransactionAsync(cancellationToken);
            var result = await operation(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }

        private void ApplyAuditFields()
        {
            DateTime? stamp = null;

            foreach (EntityEntry<BaseEntity> entry in ChangeTracker.Entries<BaseEntity>())
            {
                switch (entry.State)
                {
                    case EntityState.Added:
                        stamp ??= NextUtcNow();
                        entry.Entity.MarkCreated(stamp.Value);
                        break;
                    case EntityState.Modified:
                        stamp ??= NextUtcNow();
                        entry.Entity.MarkUpdated(stamp.Value);
                        break;
                }
            }
        }

        private DateTime NextUtcNow()
        {
            _utcNow = _utcNow.AddSeconds(1);
            return _utcNow;
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Product>(b =>
            {
                b.HasKey(p => p.Id);
                b.Property(p => p.Price)
                    .HasConversion(money => money.Amount, value => new Money(value));
            });

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
                b.Property(i => i.UnitPrice)
                    .HasConversion(money => money.Amount, value => new Money(value));
                b.Ignore(i => i.LineTotal);
            });

            base.OnModelCreating(modelBuilder);
        }
    }
}
