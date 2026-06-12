using System;
using System.Collections.Generic;

namespace CleanArchitecture.Domain.Common
{
    public abstract class BaseEntity
    {
        private readonly List<IDomainEvent> _domainEvents = new List<IDomainEvent>();

        public Guid Id { get; protected set; } = Guid.CreateVersion7();
        public DateTime CreatedAt { get; protected set; }
        public DateTime? UpdatedAt { get; protected set; }

        // Events raised since the entity was loaded/created, drained by Infrastructure into the
        // outbox during SaveChanges. AsReadOnly so callers can't mutate the list behind our back.
        public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

        public void MarkCreated(DateTime utcNow) => CreatedAt = utcNow;
        public void MarkUpdated(DateTime utcNow) => UpdatedAt = utcNow;

        protected void RaiseDomainEvent(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);

        public void ClearDomainEvents() => _domainEvents.Clear();
    }
}
