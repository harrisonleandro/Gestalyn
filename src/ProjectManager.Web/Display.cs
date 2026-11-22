using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Microsoft.Extensions.Options;
using ProjectManager.Web.Api;

namespace ProjectManager.Web;

/// <summary>Configuração de exibição (seção <c>Display</c>).</summary>
public sealed class DisplayOptions
{
    public const string SectionName = "Display";

    /// <summary>Fuso para datas com hora. Prazos (datas sem hora) não têm fuso.</summary>
    [Required]
    public string TimeZone { get; set; } = "America/Sao_Paulo";
}

/// <summary>Formatos em pt-BR e nomes das enumerações da API na interface.</summary>
public sealed class Display(IOptions<DisplayOptions> options)
{
    public static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("pt-BR");

    private readonly TimeZoneInfo _timeZone = TimeZoneInfo.FindSystemTimeZoneById(options.Value.TimeZone);

    public string DateTime(DateTimeOffset value) => TimeZoneInfo.ConvertTime(value, _timeZone).ToString("dd/MM/yyyy HH:mm", Culture);

    public string Date(DateTimeOffset value) => TimeZoneInfo.ConvertTime(value, _timeZone).ToString("dd/MM/yyyy", Culture);

    public static string Date(DateOnly? value) => value?.ToString("dd/MM/yyyy", Culture) ?? "—";

    public static string Percent(double? value) => value is { } percent ? percent.ToString("0.#", Culture) + "%" : "Sem dados";

    public static string Name(Priority value) => value switch
    {
        Priority.Low => "Baixa",
        Priority.Medium => "Média",
        Priority.High => "Alta",
        _ => "Crítica",
    };

    public static string Name(ProjectRole value) => value switch
    {
        ProjectRole.Manager => "Gerente",
        ProjectRole.Contributor => "Colaborador",
        _ => "Observador",
    };

    public static string Name(StateCategory value) => value switch
    {
        StateCategory.Todo => "A fazer",
        StateCategory.InProgress => "Em andamento",
        StateCategory.Blocked => "Bloqueada",
        StateCategory.Done => "Concluída",
        _ => "Cancelada",
    };

    public static string Name(DecisionStatus value) => value switch
    {
        DecisionStatus.Proposed => "Proposta",
        DecisionStatus.Accepted => "Aceita",
        DecisionStatus.Rejected => "Rejeitada",
        _ => "Substituída",
    };

    public static string Name(ActivityLogKind value) => value switch
    {
        ActivityLogKind.Note => "Nota",
        ActivityLogKind.Meeting => "Reunião",
        _ => "Outro",
    };

    public static string Name(ActivityOrigin value) => value switch
    {
        ActivityOrigin.Web => "Web",
        ActivityOrigin.Cli => "CLI",
        ActivityOrigin.Mcp => "MCP",
        ActivityOrigin.System => "Sistema",
        _ => "API",
    };

    public static string Name(AgentStatus value) => value == AgentStatus.Active ? "Ativo" : "Desativado";

    public static string Name(AgentCredentialState value) => value switch
    {
        AgentCredentialState.Active => "Ativa",
        AgentCredentialState.Expired => "Expirada",
        _ => "Revogada",
    };

    public static string Name(AgentAuthorizationResult value) => value switch
    {
        AgentAuthorizationResult.Allowed => "Permitida",
        AgentAuthorizationResult.CredentialRevoked => "Credencial revogada",
        AgentAuthorizationResult.CredentialExpired => "Credencial expirada",
        AgentAuthorizationResult.AgentDisabled => "Agente desativado",
        AgentAuthorizationResult.OrganizationMismatch => "Organização divergente",
        AgentAuthorizationResult.ProjectNotVisible => "Projeto sem acesso",
        AgentAuthorizationResult.ScopeMissing => "Escopo ausente",
        AgentAuthorizationResult.PermissionMissing => "Permissão ausente",
        AgentAuthorizationResult.BusinessRuleDenied => "Recusada por regra",
        _ => "Limite de requisições",
    };

    public static string Scope(string code) => AgentScopes.All.FirstOrDefault(scope => scope.Code == code).Label ?? code;

    /// <summary>Autor de algo: o nome da pessoa, ou "Agente: nome" (ADR-016, D13).</summary>
    public static string Actor(ActorReference? actor) => actor switch
    {
        null => "—",
        { Kind: ActorKind.Agent } => $"Agente: {actor.Name ?? "removido"}",
        _ => actor.Name ?? "Usuário removido",
    };

    public static string Actor(string? name, ActorKind kind, bool hasActor) => kind == ActorKind.Agent
        ? $"Agente: {name ?? "removido"}"
        : name ?? (hasActor ? "Usuário removido" : "Sistema");

    /// <summary>Teste e CI: nesta fase, a API nunca os verifica (D8).</summary>
    public static string TestsLevel(string level) => level == WorkReportLevels.NotVerified ? "Testes não verificados" : "Testes verificados";

    public static string CiLevel(string level) => level == WorkReportLevels.NotVerified ? "CI não verificado" : "CI confirmado";

    public string ApprovalLevel(WorkReportEvidence evidence, WorkReportApproval? approval) => evidence.HumanApproval switch
    {
        WorkReportLevels.Pending => "Aprovação humana pendente",
        WorkReportLevels.Approved when approval is not null =>
            $"Aprovado por {approval.ApprovedByName ?? "usuário removido"} em {DateTime(approval.ApprovedAt)}",
        WorkReportLevels.Approved => "Aprovado por uma pessoa",
        _ => "Aprovação humana não se aplica (relatório sem tarefa)",
    };

    public static string InvitationStatus(string status) => status switch
    {
        "Pending" => "Pendente",
        "Accepted" => "Aceito",
        "Revoked" => "Revogado",
        "Expired" => "Expirado",
        _ => status,
    };

    /// <summary>Classe de cor da categoria (sempre acompanhada do nome: a cor nunca é a única informação).</summary>
    public static string CategoryClass(StateCategory value) => "pm-cat-" + value.ToString().ToLowerInvariant();

    public static string Size(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => (bytes / 1024.0).ToString("0.#", Culture) + " KB",
        _ => (bytes / (1024.0 * 1024.0)).ToString("0.#", Culture) + " MB",
    };
}
