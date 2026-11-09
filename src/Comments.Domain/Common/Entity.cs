namespace Comments.Domain.Common;

/// <summary>
/// Base class for entities that raise domain events. The persistence layer collects the events on save
/// and stores them in the outbox within the same transaction, so a committed change is never lost for subscribers.
/// </summary>
public abstract class Entity
{
    private readonly List<IDomainEvent> _domainEvents = [];

    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents;

    protected void Raise(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);

    public void ClearDomainEvents() => _domainEvents.Clear();
}

public interface IDomainEvent
{
    DateTimeOffset OccurredAt { get; }
}
