namespace ProjectManager.Domain.Projects;

/// <summary>Papéis fixos da equipe de um projeto (ADR-010).</summary>
public enum ProjectRole
{
    Manager,
    Contributor,
    Viewer,
}

/// <summary>
/// Catálogo de permissões de projeto. Como na organização, a autorização é feita por permissão, nunca
/// pelo nome do papel.
/// </summary>
public static class ProjectPermissions
{
    public const string Read = "project.read";
    public const string Update = "project.update";
    public const string Archive = "project.archive";
    public const string MembersManage = "project.members.manage";

    public static IReadOnlyList<string> All { get; } = [Read, Update, Archive, MembersManage];

    private static readonly IReadOnlyList<string> ContributorPermissions = [Read, Update];
    private static readonly IReadOnlyList<string> ViewerPermissions = [Read];

    /// <summary>
    /// Gerente: tudo no projeto. Colaborador: ler e editar dados, etiquetas e repositórios.
    /// Observador: somente leitura.
    /// </summary>
    public static IReadOnlyList<string> For(ProjectRole role) => role switch
    {
        ProjectRole.Manager => All,
        ProjectRole.Contributor => ContributorPermissions,
        ProjectRole.Viewer => ViewerPermissions,
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Papel de projeto desconhecido."),
    };
}
