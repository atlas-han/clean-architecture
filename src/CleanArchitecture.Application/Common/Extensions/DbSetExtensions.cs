using System;
using System.Threading;
using System.Threading.Tasks;
using CleanArchitecture.Application.Common.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace CleanArchitecture.Application.Common.Extensions
{
    public static class DbSetExtensions
    {
        // Loads an entity by its Guid primary key, throwing NotFoundException when absent.
        // Collapses the "FindAsync → null-check → throw NotFound" guard repeated across the
        // Cancel/Confirm/Update/Delete command handlers into one place; the thrown name matches
        // the entity type (e.g. "Product", "Order") so the §6.2 NotFound→404 mapping is unchanged.
        public static async Task<TEntity> FindOrThrowAsync<TEntity>(
            this DbSet<TEntity> set, Guid id, CancellationToken cancellationToken)
            where TEntity : class
        {
            var entity = await set.FindAsync(new object[] { id }, cancellationToken);
            if (entity is null)
                throw new NotFoundException(typeof(TEntity).Name, id);

            return entity;
        }
    }
}
