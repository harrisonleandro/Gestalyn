using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using ProjectManager.Application.Agents;
using ProjectManager.Application.Documents;
using ProjectManager.Application.Organizations;
using ProjectManager.Application.Planning;
using ProjectManager.Application.Projects;
using ProjectManager.Application.Tasks;
using ProjectManager.Application.Workflows;
using ProjectManager.Domain.Agents;
using ProjectManager.IntegrationTests.Agents;
using ProjectManager.IntegrationTests.Infrastructure;
using ProjectManager.IntegrationTests.Organizations;
using ProjectManager.IntegrationTests.Projects;

namespace ProjectManager.IntegrationTests.Activity;

/// <summary>
/// Cobertura da auditoria (ADR-014, D2): toda rota de escrita da API gera exatamente um evento, com a ação
/// esperada. Uma rota nova sem evento, ou fora deste cenário, faz o teste falhar.
/// </summary>
public sealed class ActivityCoverageTests : IDisposable
{
    /// <summary>Rotas de escrita que não geram evento, com o motivo.</summary>
    private static readonly Dictionary<string, string> Exceptions = new(StringComparer.Ordinal)
    {
        ["POST /api/v1/auth/register"] = "autenticação: fica nos logs estruturados (Fase 13)",
        ["POST /api/v1/auth/confirm-email"] = "autenticação",
        ["POST /api/v1/auth/resend-confirmation"] = "autenticação",
        ["POST /api/v1/auth/login"] = "autenticação",
        ["POST /api/v1/auth/refresh"] = "autenticação",
        ["POST /api/v1/auth/logout"] = "autenticação",
        ["POST /api/v1/auth/forgot-password"] = "autenticação",
        ["POST /api/v1/auth/reset-password"] = "autenticação",
        ["POST /api/v1/projects/{projectId:guid}/activities"] = "o próprio registro manual é a atividade",
        ["POST /api/v1/agents/activities"] = "o próprio registro manual é a atividade (agentes)",
    };

    private static readonly string[] WebLabel = ["web"];

    private static readonly string[] ReadScope = ["tasks:read"];

    private static readonly string[] AllAgentScopes = [.. AgentScopes.All];

    private readonly ApiFactory _factory;
    private readonly ActivityTestKit _kit;
    private readonly AgentTestKit _agentKit;
    private readonly HashSet<string> _covered = new(StringComparer.Ordinal);

    private TestUser _owner = null!;
    private Guid _organizationId;

    public ActivityCoverageTests(PostgresDatabaseFixture database, MailpitFixture mailpit)
    {
        _factory = new ApiFactory(database.ConnectionString, mailpit);
        _kit = new ActivityTestKit(_factory.CreateClient(), mailpit);
        _agentKit = new AgentTestKit(_factory.CreateClient(), mailpit);
    }

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    private ProjectTestKit Projects => _kit.Projects;

    private OrganizationTestKit Organizations => _kit.Organizations;

    [Fact]
    public async Task EveryWriteRoute_RecordsExactlyOneEvent()
    {
        _owner = await Organizations.CreateUserAsync();
        var member = await Organizations.CreateUserAsync();
        var leaver = await Organizations.CreateUserAsync();

        // Organização: o primeiro evento da organização nova é a própria criação.
        var organization = await Organizations.CreateOrganizationAsync(_owner);
        _organizationId = organization.Id;
        var first = await _kit.ListOrganizationAsync(_owner, _organizationId);
        Assert.Equal("organization.created", Assert.Single(first.Items).Action);
        _covered.Add("POST /api/v1/organizations");

        var org = $"/api/v1/organizations/{_organizationId}";
        await ExpectAsync("PATCH /api/v1/organizations/{organizationId:guid}", "organization.updated", HttpMethod.Patch, org, new { name = "Renomeada" });

        var memberRole = await Organizations.RoleIdAsync(_owner, _organizationId, "Member");
        await ExpectAsync("POST /api/v1/organizations/{organizationId:guid}/invitations", "member.invited",
            HttpMethod.Post, $"{org}/invitations", new { email = "revogado@teste.gestalyn", roleId = memberRole });
        var invitations = await ReadAsync<List<InvitationDetails>>(HttpMethod.Get, $"{org}/invitations");
        await ExpectAsync("DELETE /api/v1/organizations/{organizationId:guid}/invitations/{invitationId:guid}", "invitation.revoked",
            HttpMethod.Delete, $"{org}/invitations/{invitations.Single().Id}");

        using (var invite = await Organizations.InviteAsync(_owner, _organizationId, member.Email, memberRole))
        {
            invite.EnsureSuccessStatusCode();
        }

        var token = await Organizations.InvitationTokenAsync(member.Email);
        await ExpectAsync("POST /api/v1/invitations/accept", "invitation.accepted", () => Organizations.AcceptAsync(member, token));
        await Organizations.AddMemberAsync(_owner, _organizationId, leaver, "Member");

        var viewerRole = await Organizations.RoleIdAsync(_owner, _organizationId, "Viewer");
        await ExpectAsync("PATCH /api/v1/organizations/{organizationId:guid}/members/{userId:guid}", "member.role_changed",
            HttpMethod.Patch, $"{org}/members/{leaver.Id}", new { roleId = viewerRole });

        // Projeto e equipe.
        var project = await ReadAfterExpectAsync<ProjectDetails>("POST /api/v1/organizations/{organizationId:guid}/projects", "project.created",
            HttpMethod.Post, $"{org}/projects", new { name = "Portal" });
        var p = $"/api/v1/projects/{project.Id}";
        await ExpectAsync("PATCH /api/v1/projects/{projectId:guid}", "project.updated", HttpMethod.Patch, p, new { description = "Novo portal", tags = WebLabel });
        await ExpectAsync("POST /api/v1/projects/{projectId:guid}/members", "project.member_added",
            HttpMethod.Post, $"{p}/members", new { userId = member.Id, role = "Contributor" });
        await ExpectAsync("PATCH /api/v1/projects/{projectId:guid}/members/{userId:guid}", "project.member_updated",
            HttpMethod.Patch, $"{p}/members/{member.Id}", new { isResponsible = true });
        var repository = await ReadAfterExpectAsync<ProjectRepositoryDetails>("POST /api/v1/projects/{projectId:guid}/repositories", "project.repository_added",
            HttpMethod.Post, $"{p}/repositories", new { url = "https://github.com/exemplo/portal" });
        await ExpectAsync("DELETE /api/v1/projects/{projectId:guid}/repositories/{repositoryId:guid}", "project.repository_removed",
            HttpMethod.Delete, $"{p}/repositories/{repository.Id}");

        // Planejamento.
        var phase = await ReadAfterExpectAsync<PhaseDetails>("POST /api/v1/projects/{projectId:guid}/phases", "phase.created",
            HttpMethod.Post, $"{p}/phases", new { name = "Descoberta" });
        var secondPhase = await ReadAsync<PhaseDetails>(HttpMethod.Post, $"{p}/phases", new { name = "Construção" });
        await ExpectAsync("PATCH /api/v1/projects/{projectId:guid}/phases/{phaseId:guid}", "phase.updated",
            HttpMethod.Patch, $"{p}/phases/{phase.Id}", new { name = "Descoberta e pesquisa" });
        await ExpectAsync("PUT /api/v1/projects/{projectId:guid}/phases/order", "phase.reordered",
            HttpMethod.Put, $"{p}/phases/order", new { phaseIds = new[] { secondPhase.Id, phase.Id } });
        await ExpectAsync("DELETE /api/v1/projects/{projectId:guid}/phases/{phaseId:guid}", "phase.deleted", HttpMethod.Delete, $"{p}/phases/{secondPhase.Id}");
        var milestone = await ReadAfterExpectAsync<MilestoneDetails>("POST /api/v1/projects/{projectId:guid}/milestones", "milestone.created",
            HttpMethod.Post, $"{p}/milestones", new { name = "Lançamento" });
        await ExpectAsync("PATCH /api/v1/projects/{projectId:guid}/milestones/{milestoneId:guid}", "milestone.updated",
            HttpMethod.Patch, $"{p}/milestones/{milestone.Id}", new { isReached = true });
        await ExpectAsync("DELETE /api/v1/projects/{projectId:guid}/milestones/{milestoneId:guid}", "milestone.deleted",
            HttpMethod.Delete, $"{p}/milestones/{milestone.Id}");

        // Workflow.
        await ExpectAsync("PUT /api/v1/projects/{projectId:guid}/workflow", "workflow.updated", HttpMethod.Put, $"{p}/workflow", new { requireIndependentApproval = true });
        var canceled = await ReadAfterExpectAsync<WorkflowStateDetails>("POST /api/v1/projects/{projectId:guid}/workflow/states", "workflow.state_added",
            HttpMethod.Post, $"{p}/workflow/states", new { name = "Cancelada", category = "Canceled" });
        await ExpectAsync("PATCH /api/v1/workflow/states/{stateId:guid}", "workflow.state_updated",
            HttpMethod.Patch, $"/api/v1/workflow/states/{canceled.Id}", new { name = "Descartada" });
        await ExpectAsync("POST /api/v1/workflow/states/{stateId:guid}/move", "workflow.state_moved",
            HttpMethod.Post, $"/api/v1/workflow/states/{canceled.Id}/move", new { position = 2 });
        var workflow = await ReadAsync<WorkflowDetails>(HttpMethod.Get, $"{p}/workflow");
        var todo = workflow.States.Single(s => s.Name == "A fazer");
        var transition = await ReadAfterExpectAsync<WorkflowTransitionDetails>("POST /api/v1/workflow/transitions", "workflow.transition_added",
            HttpMethod.Post, "/api/v1/workflow/transitions", new { fromStateId = todo.Id, toStateId = canceled.Id, requiresApproval = false });
        await ExpectAsync("DELETE /api/v1/workflow/transitions/{transitionId:guid}", "workflow.transition_removed",
            HttpMethod.Delete, $"/api/v1/workflow/transitions/{transition.Id}");
        await ExpectAsync("DELETE /api/v1/workflow/states/{stateId:guid}", "workflow.state_deleted", HttpMethod.Delete, $"/api/v1/workflow/states/{canceled.Id}");

        // Tarefas.
        var task = await ReadAfterExpectAsync<TaskDetails>("POST /api/v1/projects/{projectId:guid}/tasks", "task.created",
            HttpMethod.Post, $"{p}/tasks", new { title = "Tela de login" });
        var blocker = await ReadAsync<TaskDetails>(HttpMethod.Post, $"{p}/tasks", new { title = "Contrato da API" });
        var t = $"/api/v1/tasks/{task.Id}";
        await ExpectAsync("PATCH /api/v1/tasks/{taskId:guid}", "task.updated", HttpMethod.Patch, t, new { title = "Tela de entrada", labels = WebLabel });
        await ExpectAsync("POST /api/v1/tasks/{taskId:guid}/assignments", "task.assignee_added", HttpMethod.Post, $"{t}/assignments", new { userId = member.Id });
        await ExpectAsync("DELETE /api/v1/tasks/{taskId:guid}/assignments/{userId:guid}", "task.assignee_removed", HttpMethod.Delete, $"{t}/assignments/{member.Id}");
        await ExpectAsync("POST /api/v1/tasks/{taskId:guid}/dependencies", "task.dependency_added",
            HttpMethod.Post, $"{t}/dependencies", new { dependsOnTaskId = blocker.Id });
        await ExpectAsync("DELETE /api/v1/tasks/{taskId:guid}/dependencies/{dependsOnTaskId:guid}", "task.dependency_removed",
            HttpMethod.Delete, $"{t}/dependencies/{blocker.Id}");
        await ExpectAsync("POST /api/v1/tasks/{taskId:guid}/transitions", "task.transitioned", HttpMethod.Post, $"{t}/transitions", new { toStateId = todo.Id });
        var comment = await ReadAfterExpectAsync<CommentDetails>("POST /api/v1/tasks/{taskId:guid}/comments", "task.comment_added",
            HttpMethod.Post, $"{t}/comments", new { body = "Primeira versão" });
        await ExpectAsync("PATCH /api/v1/tasks/{taskId:guid}/comments/{commentId:guid}", "task.comment_edited",
            HttpMethod.Patch, $"{t}/comments/{comment.Id}", new { body = "Segunda versão" });
        await ExpectAsync("DELETE /api/v1/tasks/{taskId:guid}/comments/{commentId:guid}", "task.comment_deleted", HttpMethod.Delete, $"{t}/comments/{comment.Id}");
        var attachment = await ReadAfterExpectAsync<AttachmentDetails>("POST /api/v1/tasks/{taskId:guid}/attachments", "task.attachment_added",
            () => _kit.UploadAsync(task.Id, _organizationId, _owner, "tela.png", ActivityTestKit.PngBytes));
        await ExpectAsync("DELETE /api/v1/tasks/{taskId:guid}/attachments/{attachmentId:guid}", "task.attachment_deleted",
            HttpMethod.Delete, $"{t}/attachments/{attachment.Id}");
        await ExpectAsync("DELETE /api/v1/tasks/{taskId:guid}", "task.archived", HttpMethod.Delete, $"/api/v1/tasks/{blocker.Id}");

        // Documentos e decisões.
        var document = await ReadAfterExpectAsync<DocumentDetails>("POST /api/v1/projects/{projectId:guid}/documents", "document.created",
            HttpMethod.Post, $"{p}/documents", new { title = "Visão do produto", body = "# Visão" });
        var d = $"/api/v1/documents/{document.Id}";
        await ExpectAsync("PUT /api/v1/documents/{documentId:guid}", "document.updated", HttpMethod.Put, d, new { title = "Visão", body = "# Visão 2", expectedVersion = 1 });
        await ExpectAsync("POST /api/v1/documents/{documentId:guid}/archive", "document.archived", HttpMethod.Post, $"{d}/archive");
        await ExpectAsync("POST /api/v1/documents/{documentId:guid}/restore", "document.restored", HttpMethod.Post, $"{d}/restore");
        var decision = await ReadAfterExpectAsync<DecisionDetails>("POST /api/v1/projects/{projectId:guid}/decisions", "decision.proposed",
            HttpMethod.Post, $"{p}/decisions", new { title = "Banco de dados", context = "Precisamos persistir", outcome = "PostgreSQL" });
        var c = $"/api/v1/decisions/{decision.Id}";
        await ExpectAsync("PATCH /api/v1/decisions/{decisionId:guid}", "decision.updated", HttpMethod.Patch, c, new { consequences = "Operar o PostgreSQL" });
        await ExpectAsync("POST /api/v1/decisions/{decisionId:guid}/accept", "decision.accepted", HttpMethod.Post, $"{c}/accept");
        var rejected = await ReadAsync<DecisionDetails>(HttpMethod.Post, $"{p}/decisions", new { title = "Planilha", context = "Alternativa", outcome = "Não" });
        await ExpectAsync("POST /api/v1/decisions/{decisionId:guid}/reject", "decision.rejected", HttpMethod.Post, $"/api/v1/decisions/{rejected.Id}/reject");
        var replacement = await ReadAsync<DecisionDetails>(HttpMethod.Post, $"{p}/decisions", new { title = "Banco gerenciado", context = "Escala", outcome = "PostgreSQL gerenciado" });
        await ReadAsync<DecisionDetails>(HttpMethod.Post, $"/api/v1/decisions/{replacement.Id}/accept");
        await ExpectAsync("POST /api/v1/decisions/{decisionId:guid}/supersede", "decision.superseded",
            HttpMethod.Post, $"{c}/supersede", new { supersededById = replacement.Id });

        // Agentes (ADR-016).
        var agents = $"{org}/agents";
        var agent = await ReadAfterExpectAsync<AgentDetails>("POST /api/v1/organizations/{organizationId:guid}/agents", "agent.created",
            HttpMethod.Post, agents, new { name = "Revisor" });
        var a = $"{agents}/{agent.Id}";
        await ExpectAsync("PATCH /api/v1/organizations/{organizationId:guid}/agents/{agentId:guid}", "agent.updated", HttpMethod.Patch, a, new { description = "Revisa PRs" });
        var credential = await ReadAfterExpectAsync<CreatedAgentCredential>("POST /api/v1/organizations/{organizationId:guid}/agents/{agentId:guid}/credentials", "agent.credential_created",
            HttpMethod.Post, $"{a}/credentials", new { name = "CI", scopes = ReadScope });
        await ExpectAsync("POST /api/v1/organizations/{organizationId:guid}/agents/{agentId:guid}/credentials/{credentialId:guid}/revoke", "agent.credential_revoked",
            HttpMethod.Post, $"{a}/credentials/{credential.Credential.Id}/revoke");
        await ExpectAsync("PUT /api/v1/organizations/{organizationId:guid}/agents/{agentId:guid}/projects/{projectId:guid}", "agent.project_access_granted",
            HttpMethod.Put, $"{a}/projects/{project.Id}", new { role = "Contributor" });
        await ExpectAsync("DELETE /api/v1/organizations/{organizationId:guid}/agents/{agentId:guid}/projects/{projectId:guid}", "agent.project_access_removed",
            HttpMethod.Delete, $"{a}/projects/{project.Id}");
        await ExpectAsync("POST /api/v1/organizations/{organizationId:guid}/agents/{agentId:guid}/disable", "agent.disabled", HttpMethod.Post, $"{a}/disable");
        await ExpectAsync("POST /api/v1/organizations/{organizationId:guid}/agents/{agentId:guid}/enable", "agent.enabled", HttpMethod.Post, $"{a}/enable");

        // Escritas de agentes (ADR-016, D7): os mesmos eventos das pessoas, pelos mesmos serviços.
        await ReadAsync<AgentProjectAccessDetails>(HttpMethod.Put, $"{a}/projects/{project.Id}", new { role = "Contributor" });
        var agentToken = (await ReadAsync<CreatedAgentCredential>(HttpMethod.Post, $"{a}/credentials", new { name = "Escritas", scopes = AllAgentScopes })).Token;
        var agentTask = await ReadAfterExpectAsync<TaskDetails>("POST /api/v1/agents/tasks", "task.created",
            () => AsAgentAsync(agentToken, HttpMethod.Post, "/api/v1/agents/tasks", new { projectId = project.Id, title = "Criada pelo agente" }));
        await ExpectAsync("PATCH /api/v1/agents/tasks/{taskId:guid}", "task.updated",
            () => AsAgentAsync(agentToken, HttpMethod.Patch, $"/api/v1/agents/tasks/{agentTask.Id}", new { title = "Ajustada pelo agente", expectedVersion = 1 }));
        await ExpectAsync("POST /api/v1/agents/tasks/{taskId:guid}/transitions", "task.transitioned",
            () => AsAgentAsync(agentToken, HttpMethod.Post, $"/api/v1/agents/tasks/{agentTask.Id}/transitions", new { toStateId = todo.Id, expectedVersion = 2 }));
        await ExpectAsync("POST /api/v1/agents/reports", "work_report.received",
            () => AsAgentAsync(agentToken, HttpMethod.Post, "/api/v1/agents/reports", new { projectId = project.Id, taskId = agentTask.Id, summary = "Relatório do agente" }));

        // Saídas: da equipe, do projeto (arquivamento) e da organização.
        await ExpectAsync("DELETE /api/v1/projects/{projectId:guid}/members/{userId:guid}", "project.member_removed", HttpMethod.Delete, $"{p}/members/{member.Id}");
        await ExpectAsync("DELETE /api/v1/projects/{projectId:guid}", "project.archived", HttpMethod.Delete, p);
        await ExpectAsync("DELETE /api/v1/organizations/{organizationId:guid}/members/{userId:guid}", "member.removed", HttpMethod.Delete, $"{org}/members/{leaver.Id}");

        var writeRoutes = WriteRoutes();
        var missing = writeRoutes.Where(route => !_covered.Contains(route) && !Exceptions.ContainsKey(route)).Order(StringComparer.Ordinal).ToList();
        Assert.True(missing.Count == 0, $"Rotas de escrita sem evento verificado: {string.Join(", ", missing)}");
        Assert.Empty(_covered.Except(writeRoutes));
    }

    public void Dispose() => _factory.Dispose();

    /// <summary>Todas as rotas de escrita da API, como "MÉTODO /modelo".</summary>
    private HashSet<string> WriteRoutes()
    {
        var writeMethods = new HashSet<string>(["POST", "PUT", "PATCH", "DELETE"], StringComparer.Ordinal);
        return _factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText?.TrimStart('/').StartsWith("api/", StringComparison.Ordinal) == true)
            .SelectMany(endpoint => (endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [])
                .Where(writeMethods.Contains)
                .Select(method => $"{method} /{endpoint.RoutePattern.RawText!.TrimStart('/')}"))
            .ToHashSet(StringComparer.Ordinal);
    }

    private Task<HttpResponseMessage> AsAgentAsync(string token, HttpMethod method, string path, object body) =>
        _agentKit.SendAsAgentAsync(token, method, path, body, new Dictionary<string, string> { [IdempotencyRecord.HeaderName] = Guid.NewGuid().ToString() });

    private Task ExpectAsync(string route, string action, HttpMethod method, string path, object? body = null) =>
        ExpectAsync(route, action, () => Projects.SendAsync(method, path, _owner, body, _organizationId.ToString()));

    private async Task ExpectAsync(string route, string action, Func<Task<HttpResponseMessage>> call)
    {
        using var response = await CallAndCheckAsync(route, action, call);
    }

    private async Task<T> ReadAfterExpectAsync<T>(string route, string action, HttpMethod method, string path, object? body = null)
    {
        using var response = await CallAndCheckAsync(route, action, () => Projects.SendAsync(method, path, _owner, body, _organizationId.ToString()));
        return await ProjectTestKit.ReadAsync<T>(response);
    }

    private async Task<T> ReadAfterExpectAsync<T>(string route, string action, Func<Task<HttpResponseMessage>> call)
    {
        using var response = await CallAndCheckAsync(route, action, call);
        return await ProjectTestKit.ReadAsync<T>(response);
    }

    private async Task<HttpResponseMessage> CallAndCheckAsync(string route, string action, Func<Task<HttpResponseMessage>> call)
    {
        var before = (await _kit.ListOrganizationAsync(_owner, _organizationId, "?pageSize=1")).TotalCount;
        var response = await call();
        Assert.True(response.IsSuccessStatusCode, $"{route}: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync(CancellationToken)}");

        var after = await _kit.ListOrganizationAsync(_owner, _organizationId, "?pageSize=1");
        Assert.True(after.TotalCount == before + 1, $"{route}: esperado 1 evento novo, obtidos {after.TotalCount - before}.");
        Assert.Equal(action, Assert.Single(after.Items).Action);
        _covered.Add(route);
        return response;
    }

    private async Task<T> ReadAsync<T>(HttpMethod method, string path, object? body = null)
    {
        using var response = await Projects.SendAsync(method, path, _owner, body, _organizationId.ToString());
        response.EnsureSuccessStatusCode();
        return await ProjectTestKit.ReadAsync<T>(response);
    }
}
