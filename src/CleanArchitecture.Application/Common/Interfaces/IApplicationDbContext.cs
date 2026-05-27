using System.Threading;
using System.Threading.Tasks;
using CleanArchitecture.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CleanArchitecture.Application.Common.Interfaces
{
    public interface IApplicationDbContext
    {
        DbSet<Product> Products { get; }
        DbSet<Order> Orders { get; }

        Task<int> SaveChangesAsync(CancellationToken cancellationToken);

        Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken);
    }
}
