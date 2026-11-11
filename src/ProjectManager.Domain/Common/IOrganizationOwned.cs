namespace ProjectManager.Domain.Common;

/// <summary>
/// Entidade pertencente a uma organização (tenant). O vínculo é definido na criação e nunca muda.
/// </summary>
public interface IOrganizationOwned
{
    Guid OrganizationId { get; }
}
