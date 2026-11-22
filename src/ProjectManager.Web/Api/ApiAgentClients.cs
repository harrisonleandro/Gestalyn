namespace ProjectManager.Web.Api;

/// <summary>Gestão de agentes por pessoas com <c>agents.manage</c> (ADR-016): agentes, credenciais, projetos e operações.</summary>
public sealed class AgentsApi(GestalynApi api)
{
    public Task<ApiResult<List<AgentSummary>>> ListAsync(Guid organizationId) =>
        api.GetAsync<List<AgentSummary>>($"organizations/{organizationId}/agents");

    public Task<ApiResult<AgentDetails>> CreateAsync(Guid organizationId, string name, string? description) =>
        api.SendAsync<AgentDetails>(HttpMethod.Post, $"organizations/{organizationId}/agents", new { name, description });

    public Task<ApiResult<AgentDetails>> GetAsync(Guid organizationId, Guid agentId) =>
        api.GetAsync<AgentDetails>($"organizations/{organizationId}/agents/{agentId}");

    public Task<ApiResult> UpdateAsync(Guid organizationId, Guid agentId, string name, string? description) =>
        api.SendAsync(HttpMethod.Patch, $"organizations/{organizationId}/agents/{agentId}", new { name, description });

    public Task<ApiResult> DisableAsync(Guid organizationId, Guid agentId) =>
        api.SendAsync(HttpMethod.Post, $"organizations/{organizationId}/agents/{agentId}/disable");

    public Task<ApiResult> EnableAsync(Guid organizationId, Guid agentId) =>
        api.SendAsync(HttpMethod.Post, $"organizations/{organizationId}/agents/{agentId}/enable");

    /// <summary>A única resposta com o token completo. Quem chama só o mostra na resposta do formulário (D13).</summary>
    public Task<ApiResult<CreatedAgentCredential>> CreateCredentialAsync(Guid organizationId, Guid agentId, string name, IReadOnlyList<string> scopes, int? expiresInDays) =>
        api.SendAsync<CreatedAgentCredential>(HttpMethod.Post, $"organizations/{organizationId}/agents/{agentId}/credentials", new { name, scopes, expiresInDays });

    public Task<ApiResult> RevokeCredentialAsync(Guid organizationId, Guid agentId, Guid credentialId) =>
        api.SendAsync(HttpMethod.Post, $"organizations/{organizationId}/agents/{agentId}/credentials/{credentialId}/revoke");

    public Task<ApiResult> SetProjectAccessAsync(Guid organizationId, Guid agentId, Guid projectId, ProjectRole role) =>
        api.SendAsync(HttpMethod.Put, $"organizations/{organizationId}/agents/{agentId}/projects/{projectId}", new { role });

    public Task<ApiResult> RemoveProjectAccessAsync(Guid organizationId, Guid agentId, Guid projectId) =>
        api.SendAsync(HttpMethod.Delete, $"organizations/{organizationId}/agents/{agentId}/projects/{projectId}");

    public Task<ApiResult<Paged<AgentOperationItem>>> OperationsAsync(
        Guid organizationId, Guid agentId, AgentAuthorizationResult? result, DateOnly? from, DateOnly? to, int page, int pageSize = 25) =>
        api.GetAsync<Paged<AgentOperationItem>>($"organizations/{organizationId}/agents/{agentId}/operations{Query.Of(
            ("result", result),
            ("from", from is { } start ? new DateTimeOffset(start.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero) : null),
            ("to", to is { } end ? new DateTimeOffset(end.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero) : null),
            ("page", page <= 1 ? null : page),
            ("pageSize", pageSize))}");
}

/// <summary>Relatórios de trabalho de agentes, lidos por pessoas com <c>project.read</c> (ADR-016, D8).</summary>
public sealed class WorkReportsApi(GestalynApi api)
{
    public Task<ApiResult<Paged<WorkReportSummary>>> ListByProjectAsync(Guid organizationId, Guid projectId, int page) =>
        api.GetAsync<Paged<WorkReportSummary>>($"projects/{projectId}/reports{Query.Of(("page", page <= 1 ? null : page), ("pageSize", 25))}", organizationId);

    public Task<ApiResult<Paged<WorkReportSummary>>> ListByTaskAsync(Guid organizationId, Guid taskId) =>
        api.GetAsync<Paged<WorkReportSummary>>($"tasks/{taskId}/reports{Query.Of(("pageSize", 50))}", organizationId);

    public Task<ApiResult<WorkReportDetails>> GetAsync(Guid organizationId, Guid reportId) =>
        api.GetAsync<WorkReportDetails>($"reports/{reportId}", organizationId);
}
