namespace Manoksha.SharedKernel;

/// <summary>Base for persisted entities: immutable server-generated UUIDv7 identity.</summary>
public abstract class Entity
{
    protected Entity()
    {
        Id = Uuid7.NewGuid();
    }

    protected Entity(Guid id)
    {
        Id = id;
    }

    public Guid Id { get; private init; }
}
