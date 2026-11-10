using ProjectManager.Application.Abstractions.MultiTenancy;

namespace ProjectManager.Infrastructure.MultiTenancy;

/// <summary>
/// Contexto sem organização ativa. É o padrão até existir a resolução a partir da identidade
/// autenticada (Fase 04): falha de forma segura, sem ler nem gravar dados de organizações.
/// </summary>
internal sealed class UnresolvedTenantContext : ITenantContext
{
    public Guid? OrganizationId => null;
}
