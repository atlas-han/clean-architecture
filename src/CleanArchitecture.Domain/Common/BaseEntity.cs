using System;

namespace CleanArchitecture.Domain.Common
{
    public abstract class BaseEntity
    {
        public Guid Id { get; protected set; } = Guid.NewGuid();
        public DateTime CreatedAt { get; protected set; }
        public DateTime? UpdatedAt { get; protected set; }

        public void MarkCreated(DateTime utcNow) => CreatedAt = utcNow;
        public void MarkUpdated(DateTime utcNow) => UpdatedAt = utcNow;
    }
}
