using System.Net;
using ProjectManager.Application.Agents;
using ProjectManager.Application.Common;
using ProjectManager.Application.Tasks;
using ProjectManager.Domain.Agents;
using ProjectManager.Domain.Projects;
using ProjectManager.Domain.Workflows;
using ProjectManager.IntegrationTests.Infrastructure;
using ProjectManager.IntegrationTests.Organizations;
using ProjectManager.IntegrationTests.Projects;

namespace ProjectManager.IntegrationTests.Agents;

/// <summary>
/// Rotas de leitura de agentes (ADR-016, D7, etapa 7): projetos concedidos, contexto limitado e sem dados pessoais,
/// lista e detalhes de tarefas. Cada rota exige o escopo dela; projeto sem concessão ou de outra organização é 404.
/// </summary>
public sealed class AgentReadRoutesTests : IAsyncLifetime
{
    private static readonly string[] AllScopes = [.. AgentScopes.All];
    private static readonly string[] ContextOnly = [AgentScopes.ContextRead];
    private static readonly string[] TasksReadOnly = [AgentScopes.TasksRead];

    private readonly ApiFactory _factory;
    private readonly AgentTestKit _kit;

    private TestUser _owner = null!;
    private Guid _organizationId;
    private Guid _agentId;
    private Guid _contributorProject;
    private Guid _viewerProject;
    private Guid _ungrantedProject;
    private Guid _taskInContributorProject;
    private Guid _taskInUngrantedProject;
    private Guid _foreignProject;
    private Guid _foreignTask;

    public AgentReadRoutesTests(PostgresDatabaseFixture database, MailpitFixture mailpit)
    {
        _factory = new ApiFactory(database.ConnectionString, mailpit);
        _kit = new AgentTestKit(_factory.CreateClient(), mailpit);
    }

    private ProjectTestKit Projects => _kit.Projects;

    public async ValueTask InitializeAsync()
    {
        (_owner, _organizationId) = await _kit.CreateOwnerAsync();
        _contributorProject = (await Projects.CreateProjectAsync(_owner, _organizationId, new { name = "Concedido como Colaborador" })).Id;
        _viewerProject = (await Projects.CreateProjectAsync(_owner, _organizationId, new { name = "Concedido como Observador" })).Id;
        _ungrantedProject = (await Projects.CreateProjectAsync(_owner, _organizationId, new { name = "Sem concessão" })).Id;
        _taskInContributorProject = await CreateTaskAsync(_owner, _organizationId, _contributorProject, "Tarefa visível");
        _taskInUngrantedProject = await CreateTaskAsync(_owner, _organizationId, _ungrantedProject, "Tarefa invisível");

        _agentId = (await _kit.CreateAgentAsync(_owner, _organizationId, "Agente leitor")).Id;
        await GrantAsync(_contributorProject, "Contributor");
        await GrantAsync(_viewerProject, "Viewer");

        var (foreignOwner, foreignOrganization) = await _kit.CreateOwnerAsync();
        _foreignProject = (await Projects.CreateProjectAsync(foreignOwner, foreignOrganization)).Id;
        _foreignTask = await CreateTaskAsync(foreignOwner, foreignOrganization, _foreignProject, "Tarefa de outra organização");
    }

    [Fact]
    public async Task Projects_ListsOnlyTheGrantedOnes_WithTheRole_AndRequireContextRead()
    {
        var token = await TokenAsync(ContextOnly);

        var projects = await GetAsync<List<AgentProjectSummary>>(token, "/api/v1/agents/projects");

        Assert.Equal(
            [(_contributorProject, ProjectRole.Contributor), (_viewerProject, ProjectRole.Viewer)],
            projects.Select(p => (p.Id, p.Role)).OrderBy(p => p.Role));
        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(await TokenAsync(TasksReadOnly), "/api/v1/agents/projects"));
    }

    [Fact]
    public async Task Context_HasWorkflowProgressDecisionsDocumentsAndActivities_WithoutPersonalData()
    {
        await SeedContextAsync();
        var token = await TokenAsync(AllScopes);

        using var response = await _kit.SendAsAgentAsync(token, HttpMethod.Get, $"/api/v1/agents/projects/{_contributorProject}/context");
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var context = System.Text.Json.JsonSerializer.Deserialize<AgentProjectContext>(body, ProjectTestKit.Json)!;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal((_contributorProject, ProjectRole.Contributor, false), (context.Project.Id, context.Role, context.Project.IsArchived));
        Assert.Equal([ProjectPermissions.DocumentsWrite, ProjectPermissions.Read, ProjectPermissions.TasksWrite], context.Permissions);

        // Workflow: o agente nunca tem disponível uma transição com aprovação nem uma entrada em Concluída.
        var done = context.States.Single(s => s.Category == StateCategory.Done).Id;
        Assert.NotEmpty(context.Transitions);
        Assert.All(context.Transitions.Where(t => t.RequiresApproval || t.ToStateId == done), t => Assert.False(t.AvailableToAgent));
        Assert.Contains(context.Transitions, t => t.AvailableToAgent);

        Assert.Equal(1, context.Progress.Project.Counts.Total);
        Assert.Equal(["Decisão aceita"], context.AcceptedDecisions.Select(d => d.Title));
        Assert.Equal(AgentProjectContext.OutcomeMaxLength, context.AcceptedDecisions.Single().Outcome.Length);
        Assert.Equal(["Visão do produto"], context.Documents.Select(d => d.Title));
        Assert.Equal(AgentProjectContext.MaxActivities, context.RecentActivities.Count);

        // Sem conteúdo de documentos nem dados pessoais além do nome de autores.
        Assert.DoesNotContain("conteúdo secreto do documento", body, StringComparison.Ordinal);
        Assert.DoesNotContain(_owner.Email, body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"members\"", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Decisão proposta", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Viewer_ReadsTheContext_ButNoTransitionIsAvailable()
    {
        var context = await GetAsync<AgentProjectContext>(await TokenAsync(AllScopes), $"/api/v1/agents/projects/{_viewerProject}/context");

        Assert.Equal(ProjectRole.Viewer, context.Role);
        Assert.Equal([ProjectPermissions.Read], context.Permissions);
        Assert.All(context.Transitions, t => Assert.False(t.AvailableToAgent));
    }

    [Fact]
    public async Task EveryReadRoute_IsNotFound_ForUngrantedOrForeignProjects_AndForbidden_WithoutItsScope()
    {
        var all = await TokenAsync(AllScopes);
        var contextOnly = await TokenAsync(ContextOnly);
        var tasksOnly = await TokenAsync(TasksReadOnly);

        (string Token, string Path, HttpStatusCode Expected)[] cases =
        [
            (all, $"/api/v1/agents/projects/{_ungrantedProject}/context", HttpStatusCode.NotFound),
            (all, $"/api/v1/agents/projects/{_foreignProject}/context", HttpStatusCode.NotFound),
            (all, $"/api/v1/agents/projects/{Guid.NewGuid()}/context", HttpStatusCode.NotFound),
            (all, $"/api/v1/agents/projects/{_ungrantedProject}/tasks", HttpStatusCode.NotFound),
            (all, $"/api/v1/agents/projects/{_foreignProject}/tasks", HttpStatusCode.NotFound),
            (all, $"/api/v1/agents/tasks/{_taskInUngrantedProject}", HttpStatusCode.NotFound),
            (all, $"/api/v1/agents/tasks/{_foreignTask}", HttpStatusCode.NotFound),
            (all, $"/api/v1/agents/tasks/{Guid.NewGuid()}", HttpStatusCode.NotFound),

            // Sem concessão, 404 vem antes da falta de escopo: não revela que o projeto existe.
            (contextOnly, $"/api/v1/agents/tasks/{_taskInUngrantedProject}", HttpStatusCode.NotFound),
            (tasksOnly, $"/api/v1/agents/projects/{_ungrantedProject}/context", HttpStatusCode.NotFound),

            (tasksOnly, $"/api/v1/agents/projects/{_contributorProject}/context", HttpStatusCode.Forbidden),
            (contextOnly, $"/api/v1/agents/projects/{_contributorProject}/tasks", HttpStatusCode.Forbidden),
            (contextOnly, $"/api/v1/agents/tasks/{_taskInContributorProject}", HttpStatusCode.Forbidden),
            (contextOnly, $"/api/v1/agents/projects/{_contributorProject}/context", HttpStatusCode.OK),
            (tasksOnly, $"/api/v1/agents/projects/{_contributorProject}/tasks", HttpStatusCode.OK),
            (tasksOnly, $"/api/v1/agents/tasks/{_taskInContributorProject}", HttpStatusCode.OK),
        ];

        foreach (var (token, path, expected) in cases)
        {
            var status = await StatusAsync(token, path);
            Assert.True(status == expected, $"{path} ({(token == all ? "todos" : token == contextOnly ? "context:read" : "tasks:read")}): {status}, esperado {expected}");
        }
    }

    [Fact]
    public async Task Tasks_ListAndDetails_UseTheSameRulesAsPeople_WithAgentTransitions()
    {
        var token = await TokenAsync(AllScopes);

        var list = await GetAsync<PagedResult<TaskSummary>>(token, $"/api/v1/agents/projects/{_contributorProject}/tasks?pageSize=10");
        Assert.Equal([_taskInContributorProject], list.Items.Select(t => t.Id));
        Assert.Equal(HttpStatusCode.BadRequest, await StatusAsync(token, $"/api/v1/agents/projects/{_contributorProject}/tasks?pageSize=101"));

        var task = await GetAsync<TaskDetails>(token, $"/api/v1/agents/tasks/{_taskInContributorProject}");
        Assert.Equal((1, ActorKind.User), (task.Version, task.CreatedBy.Kind));
        Assert.DoesNotContain(task.AvailableTransitions, t => t.RequiresApproval || t.ToCategory == StateCategory.Done);
        Assert.NotEmpty(task.AvailableTransitions);
        Assert.DoesNotContain(ProjectPermissions.TasksApprove, task.MyPermissions);
    }

    [Fact]
    public async Task ArchivedProject_StaysReadable_WithNoTransitionAvailable()
    {
        using (var archive = await Projects.SendToProjectAsync(HttpMethod.Delete, _viewerProject, string.Empty, _owner, _organizationId))
        {
            archive.EnsureSuccessStatusCode();
        }

        var context = await GetAsync<AgentProjectContext>(await TokenAsync(AllScopes), $"/api/v1/agents/projects/{_viewerProject}/context");

        Assert.True(context.Project.IsArchived);
        Assert.All(context.Transitions, t => Assert.False(t.AvailableToAgent));
    }

    public ValueTask DisposeAsync()
    {
        _factory.Dispose();
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Documento, decisão aceita com resultado longo, decisão só proposta e tarefas suficientes para passar de 20
    /// atividades.
    /// </summary>
    private async Task SeedContextAsync()
    {
        using (var document = await Projects.SendToProjectAsync(HttpMethod.Post, _contributorProject, "/documents", _owner, _organizationId,
            new { title = "Visão do produto", body = "# conteúdo secreto do documento" }))
        {
            document.EnsureSuccessStatusCode();
        }

        var accepted = await CreateDecisionAsync("Decisão aceita", new string('r', AgentProjectContext.OutcomeMaxLength + 200));
        using (var accept = await Projects.SendAsync(HttpMethod.Post, $"/api/v1/decisions/{accepted}/accept", _owner, organizationHeader: _organizationId.ToString()))
        {
            accept.EnsureSuccessStatusCode();
        }

        await CreateDecisionAsync("Decisão proposta", "Ainda em discussão");
        for (var i = 0; i < AgentProjectContext.MaxActivities; i++)
        {
            using var update = await Projects.SendAsync(HttpMethod.Patch, $"/api/v1/tasks/{_taskInContributorProject}", _owner,
                new { title = $"Tarefa visível {i}" }, _organizationId.ToString());
            update.EnsureSuccessStatusCode();
        }
    }

    private async Task<Guid> CreateDecisionAsync(string title, string outcome)
    {
        using var response = await Projects.SendToProjectAsync(HttpMethod.Post, _contributorProject, "/decisions", _owner, _organizationId,
            new { title, context = "Contexto", outcome });
        response.EnsureSuccessStatusCode();
        return (await ProjectTestKit.ReadAsync<IdOnly>(response)).Id;
    }

    private async Task<Guid> CreateTaskAsync(TestUser owner, Guid organizationId, Guid projectId, string title)
    {
        using var response = await Projects.SendToProjectAsync(HttpMethod.Post, projectId, "/tasks", owner, organizationId, new { title });
        response.EnsureSuccessStatusCode();
        return (await ProjectTestKit.ReadAsync<IdOnly>(response)).Id;
    }

    private async Task GrantAsync(Guid projectId, string role)
    {
        using var response = await _kit.ManageAsync(HttpMethod.Put, _owner, _organizationId, $"/{_agentId}/projects/{projectId}", new { role });
        response.EnsureSuccessStatusCode();
    }

    private async Task<string> TokenAsync(IReadOnlyList<string> scopes) =>
        (await _kit.CreateCredentialAsync(_owner, _organizationId, _agentId, scopes)).Token;

    private async Task<T> GetAsync<T>(string token, string path)
    {
        using var response = await _kit.SendAsAgentAsync(token, HttpMethod.Get, path);
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{path}: {response.StatusCode}");
        return await ProjectTestKit.ReadAsync<T>(response);
    }

    private async Task<HttpStatusCode> StatusAsync(string token, string path)
    {
        using var response = await _kit.SendAsAgentAsync(token, HttpMethod.Get, path);
        return response.StatusCode;
    }

    private sealed record IdOnly(Guid Id);
}
