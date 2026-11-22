namespace ProjectManager.Web.Api;

// Contratos de agentes e relatórios de trabalho vistos pela Web (ADR-016). Espelham o JSON da API e servem só para
// exibição; o acesso é decidido pela API.

public enum AgentStatus { Active, Disabled }

public enum AgentCredentialState { Active, Expired, Revoked }

public enum AgentAuthorizationResult
{
    Allowed,
    CredentialRevoked,
    CredentialExpired,
    AgentDisabled,
    OrganizationMismatch,
    ProjectNotVisible,
    ScopeMissing,
    PermissionMissing,
    BusinessRuleDenied,
    RateLimited,
}

public sealed record AgentSummary(
    Guid Id,
    string Name,
    string? Description,
    AgentStatus Status,
    int ActiveCredentials,
    int Projects,
    DateTimeOffset CreatedAt,
    DateTimeOffset? DisabledAt);

public sealed record AgentDetails(
    Guid Id,
    string Name,
    string? Description,
    AgentStatus Status,
    Guid CreatedByUserId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? DisabledAt,
    IReadOnlyList<AgentCredentialDetails> Credentials,
    IReadOnlyList<AgentProjectAccessDetails> Projects);

/// <summary>Credencial nas listas: o prefixo identifica, mas o token nunca volta.</summary>
public sealed record AgentCredentialDetails(
    Guid Id,
    string Name,
    string Prefix,
    IReadOnlyList<string> Scopes,
    AgentCredentialState State,
    DateTimeOffset ExpiresAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset? RevokedAt,
    DateTimeOffset? LastUsedAt);

/// <summary>
/// Resposta da criação, a única com o token completo (D13). Só existe durante a renderização da resposta do próprio
/// formulário: não vai para log, sessão, cookie, URL nem aviso.
/// </summary>
public sealed record CreatedAgentCredential(AgentCredentialDetails Credential, string Token)
{
    /// <summary>Nunca expõe o token, nem por engano num log ou numa mensagem de erro.</summary>
    public override string ToString() => $"CreatedAgentCredential {{ Credential = {Credential.Id} }}";
}

public sealed record AgentProjectAccessDetails(Guid ProjectId, string ProjectName, ProjectRole Role, DateTimeOffset GrantedAt);

public sealed record AgentOperationItem(
    Guid Id,
    Guid CredentialId,
    string? CredentialName,
    DateTimeOffset OccurredAt,
    int DurationMs,
    string HttpMethod,
    string RouteTemplate,
    Guid? ProjectId,
    Guid? TaskId,
    int StatusCode,
    AgentAuthorizationResult AuthorizationResult,
    string? DenialCode,
    string? CorrelationId,
    bool IdempotentReplay,
    ActivityOrigin ClientOrigin);

/// <summary>Os cinco níveis de evidência de um relatório (ADR-016, D8), sempre separados.</summary>
public sealed record WorkReportEvidence(bool Declared, bool Accepted, string TestsExecuted, string CiConfirmed, string HumanApproval);

public sealed record WorkReportStateSnapshot(Guid StateId, string Name);

public sealed record WorkReportTests(string? Command, int? Passed, int? Failed, int? Skipped);

public sealed record WorkReportCommit(string Sha, string? Repository);

public sealed record WorkReportApproval(Guid ApprovedById, string? ApprovedByName, DateTimeOffset ApprovedAt);

public sealed record WorkReportSummary(
    Guid Id,
    Guid ProjectId,
    Guid? TaskId,
    ActorReference Agent,
    string Summary,
    WorkReportStateSnapshot? SuggestedState,
    DateTimeOffset ReceivedAt,
    WorkReportEvidence Evidence);

public sealed record WorkReportDetails(
    Guid Id,
    Guid ProjectId,
    Guid? TaskId,
    ActorReference Agent,
    string Summary,
    string? Changes,
    IReadOnlyList<string> Files,
    WorkReportTests? Tests,
    WorkReportCommit? Commit,
    WorkReportStateSnapshot? TaskStateAtReceipt,
    WorkReportStateSnapshot? SuggestedState,
    string? RequestId,
    DateTimeOffset ReceivedAt,
    WorkReportEvidence Evidence,
    WorkReportApproval? Approval);

/// <summary>Escopos de credencial (ADR-016, D4), na ordem e com os nomes da API.</summary>
public static class AgentScopes
{
    public static IReadOnlyList<(string Code, string Label)> All { get; } =
    [
        ("context:read", "Ler o contexto dos projetos"),
        ("tasks:read", "Ler tarefas"),
        ("tasks:write", "Criar e alterar tarefas"),
        ("activities:write", "Registrar atividades"),
        ("reports:write", "Enviar relatórios de trabalho"),
    ];
}

/// <summary>Valores dos níveis de evidência devolvidos pela API.</summary>
public static class WorkReportLevels
{
    public const string NotVerified = "notVerified";
    public const string Pending = "pending";
    public const string Approved = "approved";
    public const string NotApplicable = "notApplicable";
}
