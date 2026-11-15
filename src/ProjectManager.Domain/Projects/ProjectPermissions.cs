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
    public const string Plan = "project.plan";
    public const string TasksWrite = "tasks.write";
    public const string TasksComment = "tasks.comment";
    public const string TasksArchive = "tasks.archive";
    public const string TasksApprove = "tasks.approve";
    public const string WorkflowManage = "workflow.manage";
    public const string DocumentsWrite = "documents.write";
    public const string DocumentsManage = "documents.manage";

    public static IReadOnlyList<string> All { get; } = [Read, Update, Archive, MembersManage, Plan, TasksWrite, TasksComment, TasksArchive, TasksApprove, WorkflowManage, DocumentsWrite, DocumentsManage];

    private static readonly IReadOnlyList<string> ContributorPermissions = [Read, Update, TasksWrite, TasksComment, DocumentsWrite];
    private static readonly IReadOnlyList<string> ViewerPermissions = [Read];

    /// <summary>
    /// Gerente: tudo no projeto, inclusive fases, marcos, arquivamento de tarefas, aprovação, workflow, arquivamento de
    /// documentos e decisões finais. Colaborador: ler e editar dados, etiquetas e repositórios, criar e editar tarefas,
    /// comentar, escrever documentos, propor decisões e registrar atividades. Observador: somente leitura (ADR-010,
    /// ADR-011, ADR-013 e ADR-014).
    /// </summary>
    public static IReadOnlyList<string> For(ProjectRole role) => role switch
    {
        ProjectRole.Manager => All,
        ProjectRole.Contributor => ContributorPermissions,
        ProjectRole.Viewer => ViewerPermissions,
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Papel de projeto desconhecido."),
    };
}
