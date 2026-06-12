using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CleanArchitecture.Infrastructure.Outbox
{
    // Turns entities' pending domain events into OutboxMessage rows during SaveChanges, so the
    // write that produced the events and the events themselves commit (or roll back) in one
    // transaction — the transactional-outbox guarantee against partial success. Because it hooks
    // SavingChanges rather than the relational command pipeline, it runs for every provider,
    // including the InMemory provider used in dev/test.
    public class ConvertDomainEventsToOutboxInterceptor : SaveChangesInterceptor
    {
        private readonly IDateTime _dateTime;

        public ConvertDomainEventsToOutboxInterceptor(IDateTime dateTime)
        {
            _dateTime = dateTime;
        }

        public override InterceptionResult<int> SavingChanges(
            DbContextEventData eventData, InterceptionResult<int> result)
        {
            if (eventData.Context is not null)
                ConvertDomainEvents(eventData.Context);

            return base.SavingChanges(eventData, result);
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (eventData.Context is not null)
                ConvertDomainEvents(eventData.Context);

            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }

        private void ConvertDomainEvents(DbContext context)
        {
            // Snapshot first: we add OutboxMessage entries below, and mutating the change tracker
            // while enumerating it would throw. OutboxMessage is not a BaseEntity, so the new rows
            // are never re-scanned (no recursion).
            List<BaseEntity> entities = context.ChangeTracker
                .Entries<BaseEntity>()
                .Where(e => e.Entity.DomainEvents.Count > 0)
                .Select(e => e.Entity)
                .ToList();

            foreach (BaseEntity entity in entities)
            {
                foreach (IDomainEvent domainEvent in entity.DomainEvents)
                {
                    var message = new OutboxMessage
                    {
                        AggregateId = entity.Id,
                        Type = domainEvent.GetType().Name,
                        Content = JsonSerializer.Serialize(domainEvent, domainEvent.GetType()),
                        OccurredOnUtc = _dateTime.UtcNow
                    };

                    context.Add(message);
                }

                entity.ClearDomainEvents();
            }
        }
    }
}
