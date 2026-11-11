namespace ProjectManager.Infrastructure.Organizations;

/// <summary>
/// Entrada do catálogo de permissões persistida em <c>permissions</c> (dados da migration). O catálogo
/// em si é definido no domínio (<c>OrganizationPermissions</c>); a tabela garante integridade referencial
/// das permissões atribuídas a papéis.
/// </summary>
public sealed class PermissionDefinition(string code, string description)
{
    public string Code { get; private set; } = code;

    public string Description { get; private set; } = description;
}
