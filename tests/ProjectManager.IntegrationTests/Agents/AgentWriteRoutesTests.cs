using System.Net;
using ProjectManager.Application.Activity;
using ProjectManager.Application.Common;
using ProjectManager.Application.Tasks;
using ProjectManager.Application.Workflows;
using ProjectManager.Domain.Activity;
using ProjectManager.Domain.Agents;
using ProjectManager.Domain.Workflows;
using ProjectManager.IntegrationTests.Infrastructure;
using ProjectManager.IntegrationTests.Tasks;

namespace ProjectManager.IntegrationTests.Agents;

/// <summary>
/// Rotas de escrita de agentes (ADR-016, D7 e D8, etapa 8): criação e alteração de tarefas, transições e registro
/// de atividade, pelos mesmos serviços das pessoas. O agente é o autor; alterações de tarefa exigem
/// <c>expectedVersion</c>; transições com aprovação ou para Concluída são recusadas, inclusive num atalho sem aprovação.
/// </summary>
public sealed class AgentWriteRoutesTests : IAsyncLifetime
{
    private static readonly string[] AllScopes = [.. AgentScopes.All];
    private static readonly string[] Labels = ["agente"];

    private readonly ApiFactory _factory;
    private readonly TaskTestKit _tasks;
    private readonly AgentTestKit _agents;

    private Workspace _workspace = null!;
    private Guid _agentId;
    private Guid _viewerProject;
    private Guid _ungrantedProject;
    private string _token = null!;

    public AgentWriteRoutesTests(PostgresDatabaseFixture database, MailpitFixture mailpit)
    {
        _factory = new ApiFactory(database.ConnectionString, mailpit);
        _tasks = new TaskTestKit(_factory.CreateClient(), mailpit);
        _agents = new AgentTestKit(_factory.CreateClient(), mailpit);
    }

    public async ValueTask InitializeAsync()
    {
        _workspace = await _tasks.CreateWorkspaceAsync("Projeto do agente");
        _viewerProject = (await _tasks.CreateSecondProjectAsync(_workspace, "Projeto observado")).Id;
        _ungrantedProject = (await _tasks.CreateSecondProjectAsync(_workspace, "Projeto sem concessão")).Id;

        _agentId = (await _agents.CreateAgentAsync(_workspace.Owner, _workspace.OrganizationId, "Agente escritor")).Id;
        await GrantAsync(_workspace.ProjectId, "Contributor");
        await GrantAsync(_viewerProject, "Viewer");
        _token = await TokenAsync(AllScopes);
    }

    [Fact]
    public async Task CreateTask_HasTheAgentAsAuthor_WithLocation_AndNoAssignees()
    {
        using var response = await PostAsync(_token, "/api/v1/agents/tasks", new
        {
            projectId = _workspace.ProjectId,
            title = "Criada pelo agente",
            description = "Detalhes",
            priority = "High",
            labels = Labels,
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var task = await TaskTestKit.ReadAsync<TaskDetails>(response);
        Assert.Equal(new Uri($"/api/v1/agents/tasks/{task.Id}", UriKind.Relative), response.Headers.Location);
        Assert.Equal((_agentId, ActorKind.Agent, 1), (task.CreatedBy.Id, task.CreatedBy.Kind, task.Version));
        Assert.Null(task.CreatedByUserId);
        Assert.Empty(task.Assignees);
        Assert.Equal(["agente"], task.Labels);
        Assert.False(response.Headers.Contains(IdempotencyRecord.ReplayedHeaderName));

        // A pessoa vê a mesma tarefa, com o agente como autor.
        using var asOwner = await _tasks.SendToTaskAsync(HttpMethod.Get, _workspace, task.Id, string.Empty, _workspace.Owner);
        Assert.Equal(ActorKind.Agent, (await TaskTestKit.ReadAsync<TaskDetails>(asOwner)).CreatedBy.Kind);
    }

    [Fact]
    public async Task Writes_AreRefusedByVisibilityRoleAndScope()
    {
        var (foreignOwner, foreignOrganization) = await _agents.CreateOwnerAsync();
        var foreignProject = (await _agents.Projects.CreateProjectAsync(foreignOwner, foreignOrganization)).Id;
        var viewerTask = await PersonTaskAsync(_viewerProject, "Tarefa no projeto observado");
        var ungrantedTask = await PersonTaskAsync(_ungrantedProject, "Tarefa sem concessão");
        var tasksReadOnly = await TokenAsync([AgentScopes.TasksRead]);
        var withoutActivities = await TokenAsync([AgentScopes.TasksRead, AgentScopes.TasksWrite]);

        (string Token, HttpMethod Method, string Path, object Body, HttpStatusCode Expected)[] cases =
        [
            (_token, HttpMethod.Post, "/api/v1/agents/tasks", new { projectId = _ungrantedProject, title = "x" }, HttpStatusCode.NotFound),
            (_token, HttpMethod.Post, "/api/v1/agents/tasks", new { projectId = foreignProject, title = "x" }, HttpStatusCode.NotFound),
            (_token, HttpMethod.Post, "/api/v1/agents/tasks", new { projectId = Guid.NewGuid(), title = "x" }, HttpStatusCode.NotFound),
            (_token, HttpMethod.Post, "/api/v1/agents/tasks", new { projectId = _viewerProject, title = "x" }, HttpStatusCode.Forbidden),
            (tasksReadOnly, HttpMethod.Post, "/api/v1/agents/tasks", new { projectId = _workspace.ProjectId, title = "x" }, HttpStatusCode.Forbidden),
            (_token, HttpMethod.Post, "/api/v1/agents/tasks", new { title = "Sem projeto" }, HttpStatusCode.BadRequest),
            (_token, HttpMethod.Patch, $"/api/v1/agents/tasks/{viewerTask}", new { title = "x", expectedVersion = 1 }, HttpStatusCode.Forbidden),
            (_token, HttpMethod.Patch, $"/api/v1/agents/tasks/{ungrantedTask}", new { title = "x", expectedVersion = 1 }, HttpStatusCode.NotFound),
            (_token, HttpMethod.Post, $"/api/v1/agents/tasks/{ungrantedTask}/transitions", new { toStateId = Guid.NewGuid(), expectedVersion = 1 }, HttpStatusCode.NotFound),
            (_token, HttpMethod.Post, $"/api/v1/agents/tasks/{viewerTask}/transitions", new { toStateId = Guid.NewGuid(), expectedVersion = 1 }, HttpStatusCode.Forbidden),
            (_token, HttpMethod.Post, "/api/v1/agents/activities", new { projectId = _viewerProject, kind = "Note", summary = "x" }, HttpStatusCode.Forbidden),
            (_token, HttpMethod.Post, "/api/v1/agents/activities", new { projectId = _ungrantedProject, kind = "Note", summary = "x" }, HttpStatusCode.NotFound),
            (withoutActivities, HttpMethod.Post, "/api/v1/agents/activities", new { projectId = _workspace.ProjectId, kind = "Note", summary = "x" }, HttpStatusCode.Forbidden),
        ];

        foreach (var (token, method, path, body, expected) in cases)
        {
            using var response = await _agents.SendAsAgentAsync(token, method, path, body, KeyHeader());
            Assert.True(response.StatusCode == expected, $"{method} {path}: {response.StatusCode}, esperado {expected}");
        }

        // Nenhuma das recusas criou tarefas.
        using var list = await _tasks.SendToProjectAsync(HttpMethod.Get, _workspace, "/tasks", _workspace.Owner);
        Assert.Empty((await TaskTestKit.ReadAsync<PagedResult<TaskSummary>>(list)).Items);
    }

    [Fact]
    public async Task Patch_RequiresTheExpectedVersion_AndRefusesAStaleOne()
    {
        var task = await AgentTaskAsync("Versão 1");
        var path = $"/api/v1/agents/tasks/{task.Id}";

        using (var missing = await _agents.SendAsAgentAsync(_token, HttpMethod.Patch, path, new { title = "Sem versão" }))
        {
            Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        }

        using (var current = await _agents.SendAsAgentAsync(_token, HttpMethod.Patch, path, new { title = "Versão 2", expectedVersion = 1 }))
        {
            Assert.Equal(HttpStatusCode.OK, current.StatusCode);
            Assert.Equal(2, (await TaskTestKit.ReadAsync<TaskDetails>(current)).Version);
        }

        using (var stale = await _agents.SendAsAgentAsync(_token, HttpMethod.Patch, path, new { title = "Sobrescrita", expectedVersion = 1 }))
        {
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        }

        var final = await AgentGetTaskAsync(task.Id);
        Assert.Equal(("Versão 2", 2), (final.Title, final.Version));
    }

    [Fact]
    public async Task Transitions_UseTheSameRules_AndTheAgentNeverApprovesNorEntersDone()
    {
        var workflow = await ReadWorkflowAsync();
        Guid State(string name) => workflow.States.Single(s => s.Name == name).Id;
        await AddTransitionAsync(State("Em andamento"), State("Concluída"));

        var task = await AgentTaskAsync("Fluxo do agente");
        var version = task.Version;

        Assert.Equal(HttpStatusCode.BadRequest, await TransitionAsync(task.Id, State("A fazer"), expectedVersion: null));
        Assert.Equal(HttpStatusCode.Conflict, await TransitionAsync(task.Id, State("A fazer"), version + 5));
        Assert.Equal(HttpStatusCode.OK, await TransitionAsync(task.Id, State("A fazer"), version++));
        Assert.Equal(HttpStatusCode.OK, await TransitionAsync(task.Id, State("Em andamento"), version++));

        // Atalho sem aprovação para Concluída: recusado pela regra única (403), não pela rota.
        Assert.Equal(HttpStatusCode.Forbidden, await TransitionAsync(task.Id, State("Concluída"), version));
        Assert.Equal(HttpStatusCode.OK, await TransitionAsync(task.Id, State("Em revisão"), version++));

        // Saída da revisão com aprovação: recusada.
        Assert.Equal(HttpStatusCode.Forbidden, await TransitionAsync(task.Id, State("Concluída"), version));

        var final = await AgentGetTaskAsync(task.Id);
        Assert.Equal(("Em revisão", version), (final.State.Name, final.Version));
    }

    /// <summary>Estratégia de testes, item 3: projeto arquivado continua legível pelo agente, mas recusa escritas (409).</summary>
    [Fact]
    public async Task ArchivedProject_RefusesAgentWrites_WithConflict()
    {
        var project = (await _tasks.CreateSecondProjectAsync(_workspace, "Projeto a arquivar")).Id;
        await GrantAsync(project, "Contributor");
        var task = await PersonTaskAsync(project, "Tarefa antes do arquivamento");
        using (var archive = await _tasks.Projects.SendToProjectAsync(HttpMethod.Delete, project, string.Empty, _workspace.Owner, _workspace.OrganizationId))
        {
            Assert.True(archive.IsSuccessStatusCode, $"arquivar: {archive.StatusCode}");
        }

        using (var create = await PostAsync(_token, "/api/v1/agents/tasks", new { projectId = project, title = "Depois do arquivamento" }))
        {
            Assert.Equal(HttpStatusCode.Conflict, create.StatusCode);
        }

        using (var patch = await _agents.SendAsAgentAsync(_token, HttpMethod.Patch, $"/api/v1/agents/tasks/{task}", new { title = "Alterada", expectedVersion = 1 }))
        {
            Assert.Equal(HttpStatusCode.Conflict, patch.StatusCode);
        }

        using (var activity = await PostAsync(_token, "/api/v1/agents/activities", new { projectId = project, kind = "Note", summary = "x" }))
        {
            Assert.Equal(HttpStatusCode.Conflict, activity.StatusCode);
        }

        var unchanged = await AgentGetTaskAsync(task);
        Assert.Equal(("Tarefa antes do arquivamento", 1), (unchanged.Title, unchanged.Version));
    }

    [Fact]
    public async Task ActivityLog_IsAuthoredByTheAgent()
    {
        using var response = await PostAsync(_token, "/api/v1/agents/activities", new
        {
            projectId = _workspace.ProjectId,
            kind = "Note",
            summary = "Análise concluída pelo agente",
            details = "Sem bloqueios.",
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var item = await TaskTestKit.ReadAsync<ActivityItem>(response);
        Assert.Equal((_agentId, ActorKind.Agent, ActivityLogKind.Note), (item.ActorId, item.ActorKind, item.Kind));
        Assert.Equal(_workspace.ProjectId, item.ProjectId);
    }

    public ValueTask DisposeAsync()
    {
        _factory.Dispose();
        return ValueTask.CompletedTask;
    }

    private static Dictionary<string, string> KeyHeader() => new() { [IdempotencyRecord.HeaderName] = Guid.NewGuid().ToString() };

    private Task<HttpResponseMessage> PostAsync(string token, string path, object body) =>
        _agents.SendAsAgentAsync(token, HttpMethod.Post, path, body, KeyHeader());

    private async Task<TaskDetails> AgentTaskAsync(string title)
    {
        using var response = await PostAsync(_token, "/api/v1/agents/tasks", new { projectId = _workspace.ProjectId, title });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await TaskTestKit.ReadAsync<TaskDetails>(response);
    }

    private async Task<TaskDetails> AgentGetTaskAsync(Guid taskId)
    {
        using var response = await _agents.SendAsAgentAsync(_token, HttpMethod.Get, $"/api/v1/agents/tasks/{taskId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await TaskTestKit.ReadAsync<TaskDetails>(response);
    }

    private async Task<HttpStatusCode> TransitionAsync(Guid taskId, Guid toStateId, int? expectedVersion)
    {
        using var response = await PostAsync(_token, $"/api/v1/agents/tasks/{taskId}/transitions", new { toStateId, expectedVersion });
        return response.StatusCode;
    }

    private async Task<Guid> PersonTaskAsync(Guid projectId, string title)
    {
        using var response = await _tasks.Projects.SendToProjectAsync(HttpMethod.Post, projectId, "/tasks", _workspace.Owner, _workspace.OrganizationId, new { title });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await TaskTestKit.ReadAsync<TaskDetails>(response)).Id;
    }

    private async Task<WorkflowDetails> ReadWorkflowAsync()
    {
        using var response = await _tasks.SendToProjectAsync(HttpMethod.Get, _workspace, "/workflow", _workspace.Owner);
        return await TaskTestKit.ReadAsync<WorkflowDetails>(response);
    }

    private async Task AddTransitionAsync(Guid from, Guid to)
    {
        using var response = await _tasks.Projects.SendAsync(
            HttpMethod.Post,
            "/api/v1/workflow/transitions",
            _workspace.Owner,
            new { fromStateId = from, toStateId = to, requiresApproval = false },
            _workspace.OrganizationId.ToString());
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private async Task GrantAsync(Guid projectId, string role)
    {
        using var response = await _agents.ManageAsync(HttpMethod.Put, _workspace.Owner, _workspace.OrganizationId, $"/{_agentId}/projects/{projectId}", new { role });
        response.EnsureSuccessStatusCode();
    }

    private async Task<string> TokenAsync(IReadOnlyList<string> scopes) =>
        (await _agents.CreateCredentialAsync(_workspace.Owner, _workspace.OrganizationId, _agentId, scopes)).Token;
}
