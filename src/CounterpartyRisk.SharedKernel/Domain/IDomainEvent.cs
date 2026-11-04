namespace CounterpartyRisk.SharedKernel.Domain;

/// <summary>
/// Something that happened in the domain, raised by an aggregate.
/// </summary>
public interface IDomainEvent
{
    DateTimeOffset OccurredOn { get; }
}
