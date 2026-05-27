using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Domain.Common;
using CleanArchitecture.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage;

namespace CleanArchitecture.Infrastructure.Persistence
{
    public class ApplicationDbContext : DbContext, IApplicationDbContext
    {
        private readonly IDateTime _dateTime;

        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options, IDateTime dateTime)
            : base(options)
        {
            _dateTime = dateTime;
        }

        public DbSet<Product> Products => Set<Product>();
        public DbSet<Order> Orders => Set<Order>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
            base.OnModelCreating(modelBuilder);
        }

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            ApplyAuditFields();
            return base.SaveChangesAsync(cancellationToken);
        }

        public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken)
        {
            return Database.BeginTransactionAsync(cancellationToken);
        }

        private void ApplyAuditFields()
        {
            foreach (EntityEntry<BaseEntity> entry in ChangeTracker.Entries<BaseEntity>())
            {
                switch (entry.State)
                {
                    case EntityState.Added:
                        entry.Entity.MarkCreated(_dateTime.UtcNow);
                        break;
                    case EntityState.Modified:
                        entry.Entity.MarkUpdated(_dateTime.UtcNow);
                        break;
                }
            }
        }
    }
}
