namespace ProjectManager.Domain.Common;

/// <summary>
/// Base das entidades do domínio. O identificador é um UUID v7, gerado no domínio e ordenável no tempo.
/// </summary>
public abstract class Entity
{
    protected Entity() => Id = Guid.CreateVersion7();

    public Guid Id { get; private set; }
}
