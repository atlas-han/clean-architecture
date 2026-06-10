using System;
using System.Threading;
using System.Threading.Tasks;
using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Domain.Common;
using CleanArchitecture.Domain.Entities;
using CleanArchitecture.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace CleanArchitecture.Application.UnitTests.TestDoubles
{
    public class TestDbContext : DbContext, IApplicationDbContext
    {
        // Deterministic, strictly-increasing test clock so that consecutive saves
        // produce distinct CreatedAt values (mirrors ApplicationDbContext + IDateTime).
        private DateTime _utcNow = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }

        public DbSet<Product> Products => Set<Product>();
        public DbSet<Order> Orders => Set<Order>();

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            ApplyAuditFields();
            return base.SaveChangesAsync(cancellationToken);
        }

        // Mirrors ApplicationDbContext.ExecuteInTransactionAsync — keep the two in sync
        // if the contract's transaction semantics ever change.
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
            // One tick per save, shared by every entry in the batch — matches
            // ApplicationDbContext, where a single SaveChanges stamps all entries
            // with the same instant. Ordering across saves stays deterministic;
            // ordering within one save is intentionally not expressible.
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
