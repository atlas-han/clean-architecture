namespace CleanArchitecture.Domain.Common
{
    // Marker for something that happened in the domain worth telling the outside world about.
    // Entities raise these; Infrastructure turns the pending events into outbox rows inside the
    // same transaction as the write. Kept dependency-free so it stays in the Domain layer.
    public interface IDomainEvent
    {
    }
}
