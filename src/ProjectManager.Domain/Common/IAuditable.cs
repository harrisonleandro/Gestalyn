namespace ProjectManager.Domain.Common;

/// <summary>
/// Entidade com datas de criação e de última alteração, em UTC, mantidas pela camada de persistência.
/// </summary>
public interface IAuditable
{
    DateTimeOffset CreatedAt { get; }

    DateTimeOffset UpdatedAt { get; }
}
