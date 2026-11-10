using ProjectManager.Domain.Common;

namespace ProjectManager.Domain.Organizations;

/// <summary>
/// Organização: raiz do tenant. Todos os dados pertencentes a ela carregam seu identificador.
/// </summary>
public sealed class Organization : Entity, IAuditable
{
    public const int NameMaxLength = 200;

    public Organization(string name) => Name = Guard.RequiredText(name, NameMaxLength, nameof(name));

    public string Name { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public void Rename(string name) => Name = Guard.RequiredText(name, NameMaxLength, nameof(name));
}
