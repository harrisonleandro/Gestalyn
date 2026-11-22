using System.Net;
using Npgsql;
using ProjectManager.Application.Agents;
using ProjectManager.Application.Common;
using ProjectManager.Application.Tasks;
using ProjectManager.Application.Workflows;
using ProjectManager.Domain.Agents;
using ProjectManager.Domain.Workflows;
using ProjectManager.IntegrationTests.Infrastructure;
using ProjectManager.IntegrationTests.Organizations;
using ProjectManager.IntegrationTests.Tasks;

namespace ProjectManager.IntegrationTests.Agents;

/// <summary>
/// Relatórios de trabalho (ADR-016, D8, etapa 9): o que o agente declara fica separado do que a API aceitou, dos
/// testes e do CI (não verificados) e da aprovação humana, calculada pelo histórico. O relatório não muda a tarefa e
/// é imutável no banco.
/// </summary>
public sealed class WorkReportRoutesTests : IAsyncLifetime
{
    private static readonly string[] AllScopes = [.. AgentScopes.All];
    private static readonly string[] ChangedFiles = ["src/App.cs", "tests/AppTests.cs"];
    private const string Sha = "0123456789ABCDEF0123456789abcdef01234567";

    private readonly PostgresDatabaseFixture _database;
    private readonly ApiFactory _factory;
    private readonly TaskTestKit _tasks;
    private readonly AgentTestKit _agents;

    private Workspace _workspace = null!;
    private Guid _agentId;
    private string _token = null!;
    private WorkflowDetails _workflow = null!;

    public WorkReportRoutesTests(PostgresDatabaseFixture database, MailpitFixture mailpit)
    {
        _database = database;
        _factory = new ApiFactory(database.ConnectionString, mailpit);
        _tasks = new TaskTestKit(_factory.CreateClient(), mailpit);
        _agents = new AgentTestKit(_factory.CreateClient(), mailpit);
    }

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _workspace = await _tasks.CreateWorkspaceAsync("Projeto com relatórios");
        _agentId = (await _agents.CreateAgentAsync(_workspace.Owner, _workspace.OrganizationId, "Agente relator")).Id;
        await GrantAsync(_workspace.ProjectId, "Contributor");
        _token = await TokenAsync(AllScopes);

        using var response = await _tasks.SendToProjectAsync(HttpMethod.Get, _workspace, "/workflow", _workspace.Owner);
        _workflow = await TaskTestKit.ReadAsync<WorkflowDetails>(response);
    }

    [Fact]
    public async Task Submit_SeparatesTheLevels_AndLeavesTheTaskUnchanged()
    {
        var task = await TaskInStateAsync("Em andamento");

        using var response = await SubmitAsync(new
        {
            projectId = _workspace.ProjectId,
            taskId = task.Id,
            summary = "Implementei a tela de entrada.",
            changes = "## Alterações\n- tela nova",
            files = ChangedFiles,
            tests = new { command = "dotnet test", passed = 12, failed = 0, skipped = 1 },
            commit = new { sha = Sha, repository = "org/app" },
            suggestedStatus = State("Em revisão"),
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var receipt = await TaskTestKit.ReadAsync<WorkReportReceipt>(response);
        Assert.Equal(new Uri($"/api/v1/reports/{receipt.ReportId}", UriKind.Relative), response.Headers.Location);
        Assert.True(receipt.Accepted);
        Assert.NotEqual(Guid.Empty, receipt.ActivityCreated);
        Assert.Equal(("Em andamento", StateCategory.InProgress), (receipt.TaskStatus!.Name, receipt.TaskStatus.Category));
        Assert.Equal((false, WorkReportLevels.DirectTransition), (receipt.ApprovalRequired, receipt.SuggestedTransition));
        Assert.Equal(
            new WorkReportEvidence(true, true, WorkReportLevels.NotVerified, WorkReportLevels.NotVerified, WorkReportLevels.Pending),
            receipt.Evidence);

        // O relatório não muda a tarefa: mesmo estado e mesma versão.
        var after = await PersonGetTaskAsync(task.Id);
        Assert.Equal((task.State.Id, task.Version), (after.State.Id, after.Version));

        // A pessoa lê o relatório completo, com o retrato do recebimento.
        var details = await PersonGetAsync<WorkReportDetails>($"/api/v1/reports/{receipt.ReportId}");
        Assert.Equal((_agentId, ActorKind.Agent, "Agente relator"), (details.Agent.Id, details.Agent.Kind, details.Agent.Name));
        Assert.Equal(ChangedFiles, details.Files);
        Assert.Equal(new WorkReportTests("dotnet test", 12, 0, 1), details.Tests);
        Assert.Equal(new WorkReportCommit(Sha.ToLowerInvariant(), "org/app"), details.Commit);
        Assert.Equal(new WorkReportStateSnapshot(State("Em andamento"), "Em andamento"), details.TaskStateAtReceipt);
        Assert.Equal(new WorkReportStateSnapshot(State("Em revisão"), "Em revisão"), details.SuggestedState);
        Assert.Null(details.Approval);

        var byTask = await PersonGetAsync<PagedResult<WorkReportSummary>>($"/api/v1/tasks/{task.Id}/reports");
        var byProject = await PersonGetAsync<PagedResult<WorkReportSummary>>($"/api/v1/projects/{_workspace.ProjectId}/reports");
        Assert.Equal([receipt.ReportId], byTask.Items.Select(r => r.Id));
        Assert.Equal([receipt.ReportId], byProject.Items.Select(r => r.Id));
    }

    [Fact]
    public async Task ApprovalRequired_FollowsTheDirectTransition_OrIsNullWithoutOne()
    {
        var inReview = await TaskInStateAsync("Em revisão");

        var toDone = await ReceiptAsync(new { projectId = _workspace.ProjectId, taskId = inReview.Id, summary = "Pronto", suggestedStatus = State("Concluída") });
        Assert.Equal((true, WorkReportLevels.DirectTransition), (toDone.ApprovalRequired, toDone.SuggestedTransition));

        var noDirect = await ReceiptAsync(new { projectId = _workspace.ProjectId, taskId = inReview.Id, summary = "Volta", suggestedStatus = State("Backlog") });
        Assert.Equal(((bool?)null, WorkReportLevels.NoTransition), (noDirect.ApprovalRequired, noDirect.SuggestedTransition));

        var withoutTask = await ReceiptAsync(new { projectId = _workspace.ProjectId, summary = "Análise do projeto", suggestedStatus = State("Concluída") });
        Assert.Equal(((bool?)null, WorkReportLevels.NoTransition, (WorkReportTaskStatus?)null), (withoutTask.ApprovalRequired, withoutTask.SuggestedTransition, withoutTask.TaskStatus));
        Assert.Equal(WorkReportLevels.NotApplicable, withoutTask.Evidence.HumanApproval);
    }

    [Fact]
    public async Task HumanApproval_IsTheFirstCompletionByAPersonAfterTheReport()
    {
        var task = await TaskInStateAsync("Em revisão");
        var before = await ReceiptAsync(new { projectId = _workspace.ProjectId, taskId = task.Id, summary = "Antes da aprovação" });

        // Uma mudança da pessoa que não é para Concluída não é aprovação.
        await PersonMoveAsync(task.Id, "Em andamento");
        await PersonMoveAsync(task.Id, "Em revisão");
        Assert.Equal(WorkReportLevels.Pending, (await PersonGetAsync<WorkReportDetails>($"/api/v1/reports/{before.ReportId}")).Evidence.HumanApproval);

        var done = await PersonMoveAsync(task.Id, "Concluída");
        var after = await ReceiptAsync(new { projectId = _workspace.ProjectId, taskId = task.Id, summary = "Depois da aprovação" });

        var approved = await PersonGetAsync<WorkReportDetails>($"/api/v1/reports/{before.ReportId}");
        Assert.Equal(WorkReportLevels.Approved, approved.Evidence.HumanApproval);
        Assert.Equal(_workspace.Owner.Id, approved.Approval!.ApprovedById);
        Assert.InRange(approved.Approval.ApprovedAt, approved.ReceivedAt, done.UpdatedAt);
        Assert.NotNull(approved.Approval.ApprovedByName);

        // A conclusão anterior ao relatório não conta: este continua pendente.
        var later = await PersonGetAsync<WorkReportDetails>($"/api/v1/reports/{after.ReportId}");
        Assert.Equal((WorkReportLevels.Pending, (WorkReportApproval?)null), (later.Evidence.HumanApproval, later.Approval));

        var list = await PersonGetAsync<PagedResult<WorkReportSummary>>($"/api/v1/tasks/{task.Id}/reports");
        Assert.Equal(
            [(after.ReportId, WorkReportLevels.Pending), (before.ReportId, WorkReportLevels.Approved)],
            list.Items.Select(r => (r.Id, r.Evidence.HumanApproval)));
    }

    [Fact]
    public async Task InvalidOrUnauthorizedReports_AreRefused_AndNothingIsStored()
    {
        var otherProject = (await _tasks.CreateSecondProjectAsync(_workspace, "Outro projeto")).Id;
        var viewerProject = (await _tasks.CreateSecondProjectAsync(_workspace, "Projeto observado")).Id;
        await GrantAsync(viewerProject, "Viewer");
        var otherTask = await PersonTaskAsync(otherProject, "Tarefa de outro projeto");
        var otherState = await OtherProjectStateAsync(otherProject);
        var withoutReports = await TokenAsync([AgentScopes.TasksRead, AgentScopes.TasksWrite]);
        var project = _workspace.ProjectId;

        (string Token, object Body, HttpStatusCode Expected)[] cases =
        [
            (_token, new { projectId = project, taskId = otherTask, summary = "x" }, HttpStatusCode.BadRequest),
            (_token, new { projectId = project, taskId = Guid.NewGuid(), summary = "x" }, HttpStatusCode.BadRequest),
            (_token, new { projectId = project, summary = "x", suggestedStatus = otherState }, HttpStatusCode.BadRequest),
            (_token, new { projectId = project, summary = "x", commit = new { sha = "abc123" } }, HttpStatusCode.BadRequest),
            (_token, new { projectId = project, summary = "x", files = Enumerable.Range(0, WorkReport.MaxFiles + 1).Select(i => $"f{i}").ToArray() }, HttpStatusCode.BadRequest),
            (_token, new { projectId = project, summary = "x", files = new[] { " " } }, HttpStatusCode.BadRequest),
            (_token, new { projectId = project, summary = "x", tests = new { passed = -1 } }, HttpStatusCode.BadRequest),
            (_token, new { projectId = project, summary = new string('s', WorkReport.SummaryMaxLength + 1) }, HttpStatusCode.BadRequest),
            (_token, new { projectId = project, summary = " " }, HttpStatusCode.BadRequest),
            (_token, new { summary = "Sem projeto" }, HttpStatusCode.BadRequest),
            (_token, new { projectId = viewerProject, summary = "x" }, HttpStatusCode.Forbidden),
            (withoutReports, new { projectId = project, summary = "x" }, HttpStatusCode.Forbidden),
            (_token, new { projectId = otherProject, summary = "x" }, HttpStatusCode.NotFound),
        ];

        for (var i = 0; i < cases.Length; i++)
        {
            var (token, body, expected) = cases[i];
            using var response = await _agents.SendAsAgentAsync(token, HttpMethod.Post, "/api/v1/agents/reports", body, KeyHeader());
            Assert.True(response.StatusCode == expected, $"caso {i}: {response.StatusCode}, esperado {expected}");
        }

        // Projeto arquivado: 409.
        await GrantAsync(otherProject, "Contributor");
        using (var archive = await _tasks.Projects.SendToProjectAsync(HttpMethod.Delete, otherProject, string.Empty, _workspace.Owner, _workspace.OrganizationId))
        {
            archive.EnsureSuccessStatusCode();
        }

        using (var archived = await SubmitAsync(new { projectId = otherProject, summary = "x" }))
        {
            Assert.Equal(HttpStatusCode.Conflict, archived.StatusCode);
        }

        Assert.Equal(0L, await CountReportsAsync());
    }

    [Fact]
    public async Task Reports_AreImmutableInTheDatabase()
    {
        var receipt = await ReceiptAsync(new { projectId = _workspace.ProjectId, summary = "Imutável" });

        Assert.Equal("23000", await ExecuteAsync("UPDATE work_reports SET summary = 'alterado' WHERE id = @id", receipt.ReportId));
        Assert.Equal("23000", await ExecuteAsync("DELETE FROM work_reports WHERE id = @id", receipt.ReportId));
        Assert.Equal("23000", await ExecuteAsync("TRUNCATE work_reports", receipt.ReportId));

        var details = await PersonGetAsync<WorkReportDetails>($"/api/v1/reports/{receipt.ReportId}");
        Assert.Equal("Imutável", details.Summary);
    }

    [Fact]
    public async Task Reading_IsLimitedToTheProjectAndTheOrganization()
    {
        var receipt = await ReceiptAsync(new { projectId = _workspace.ProjectId, summary = "Só para esta organização" });
        var (foreignOwner, foreignOrganization) = await _agents.CreateOwnerAsync();
        var foreignProject = (await _agents.Projects.CreateProjectAsync(foreignOwner, foreignOrganization)).Id;

        using (var foreign = await _tasks.Projects.SendAsync(HttpMethod.Get, $"/api/v1/reports/{receipt.ReportId}", foreignOwner, organizationHeader: foreignOrganization.ToString()))
        {
            Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        }

        using (var missing = await _tasks.Projects.SendAsync(HttpMethod.Get, $"/api/v1/reports/{Guid.NewGuid()}", _workspace.Owner, organizationHeader: _workspace.OrganizationId.ToString()))
        {
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        }

        using var foreignList = await _tasks.Projects.SendAsync(HttpMethod.Get, $"/api/v1/projects/{foreignProject}/reports", foreignOwner, organizationHeader: foreignOrganization.ToString());
        Assert.Empty((await TaskTestKit.ReadAsync<PagedResult<WorkReportSummary>>(foreignList)).Items);
    }

    [Fact]
    public async Task Resending_WithTheSameKey_ReplaysTheReceipt_WithoutASecondReport()
    {
        var key = new Dictionary<string, string> { [IdempotencyRecord.HeaderName] = Guid.NewGuid().ToString() };
        var body = new { projectId = _workspace.ProjectId, summary = "Uma vez" };

        using var first = await _agents.SendAsAgentAsync(_token, HttpMethod.Post, "/api/v1/agents/reports", body, key);
        using var second = await _agents.SendAsAgentAsync(_token, HttpMethod.Post, "/api/v1/agents/reports", body, key);

        Assert.Equal((HttpStatusCode.Created, HttpStatusCode.Created), (first.StatusCode, second.StatusCode));
        Assert.True(second.Headers.Contains(IdempotencyRecord.ReplayedHeaderName));
        Assert.Equal((await TaskTestKit.ReadAsync<WorkReportReceipt>(first)).ReportId, (await TaskTestKit.ReadAsync<WorkReportReceipt>(second)).ReportId);
        Assert.Equal(1L, await CountReportsAsync());
    }

    public ValueTask DisposeAsync()
    {
        _factory.Dispose();
        return ValueTask.CompletedTask;
    }

    private static Dictionary<string, string> KeyHeader() => new() { [IdempotencyRecord.HeaderName] = Guid.NewGuid().ToString() };

    private Guid State(string name) => _workflow.States.Single(s => s.Name == name).Id;

    private Task<HttpResponseMessage> SubmitAsync(object body) =>
        _agents.SendAsAgentAsync(_token, HttpMethod.Post, "/api/v1/agents/reports", body, KeyHeader());

    private async Task<WorkReportReceipt> ReceiptAsync(object body)
    {
        using var response = await SubmitAsync(body);
        Assert.True(response.StatusCode == HttpStatusCode.Created, $"{response.StatusCode}: {await response.Content.ReadAsStringAsync(CancellationToken)}");
        return await TaskTestKit.ReadAsync<WorkReportReceipt>(response);
    }

    /// <summary>Tarefa criada pela pessoa e levada pelo caminho padrão até o estado pedido.</summary>
    private async Task<TaskDetails> TaskInStateAsync(string target)
    {
        var task = await _tasks.CreateTaskAsync(_workspace, $"Tarefa em {target}");
        foreach (var step in new[] { "A fazer", "Em andamento", "Em revisão" })
        {
            if (task.State.Name == target)
            {
                break;
            }

            task = await PersonMoveAsync(task.Id, step);
        }

        Assert.Equal(target, task.State.Name);
        return task;
    }

    private async Task<TaskDetails> PersonMoveAsync(Guid taskId, string state)
    {
        using var response = await _tasks.SendToTaskAsync(HttpMethod.Post, _workspace, taskId, "/transitions", _workspace.Owner, new { toStateId = State(state) });
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{state}: {response.StatusCode}");
        return await TaskTestKit.ReadAsync<TaskDetails>(response);
    }

    private async Task<TaskDetails> PersonGetTaskAsync(Guid taskId)
    {
        using var response = await _tasks.SendToTaskAsync(HttpMethod.Get, _workspace, taskId, string.Empty, _workspace.Owner);
        return await TaskTestKit.ReadAsync<TaskDetails>(response);
    }

    private async Task<T> PersonGetAsync<T>(string path)
    {
        using var response = await _tasks.Projects.SendAsync(HttpMethod.Get, path, _workspace.Owner, organizationHeader: _workspace.OrganizationId.ToString());
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{path}: {response.StatusCode}");
        return await TaskTestKit.ReadAsync<T>(response);
    }

    private async Task<Guid> PersonTaskAsync(Guid projectId, string title)
    {
        using var response = await _tasks.Projects.SendToProjectAsync(HttpMethod.Post, projectId, "/tasks", _workspace.Owner, _workspace.OrganizationId, new { title });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await TaskTestKit.ReadAsync<TaskDetails>(response)).Id;
    }

    private async Task<Guid> OtherProjectStateAsync(Guid projectId)
    {
        using var response = await _tasks.Projects.SendToProjectAsync(HttpMethod.Get, projectId, "/workflow", _workspace.Owner, _workspace.OrganizationId);
        return (await TaskTestKit.ReadAsync<WorkflowDetails>(response)).States[0].Id;
    }

    private async Task GrantAsync(Guid projectId, string role)
    {
        using var response = await _agents.ManageAsync(HttpMethod.Put, _workspace.Owner, _workspace.OrganizationId, $"/{_agentId}/projects/{projectId}", new { role });
        response.EnsureSuccessStatusCode();
    }

    private async Task<string> TokenAsync(IReadOnlyList<string> scopes) =>
        (await _agents.CreateCredentialAsync(_workspace.Owner, _workspace.OrganizationId, _agentId, scopes)).Token;

    private async Task<long> CountReportsAsync()
    {
        await using var connection = new NpgsqlConnection(_database.ConnectionString);
        await connection.OpenAsync(CancellationToken);
        await using var command = new NpgsqlCommand("SELECT count(*) FROM work_reports WHERE organization_id = @organization", connection);
        command.Parameters.AddWithValue("organization", _workspace.OrganizationId);
        return (long)(await command.ExecuteScalarAsync(CancellationToken))!;
    }

    /// <summary>Executa numa transação sempre desfeita: sem o gatilho, nada some do banco compartilhado pelos testes.</summary>
    private async Task<string?> ExecuteAsync(string sql, Guid id)
    {
        await using var connection = new NpgsqlConnection(_database.ConnectionString);
        await connection.OpenAsync(CancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(CancellationToken);
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("id", id);
        try
        {
            await command.ExecuteNonQueryAsync(CancellationToken);
            return null;
        }
        catch (PostgresException exception)
        {
            return exception.SqlState;
        }
        finally
        {
            await transaction.RollbackAsync(CancellationToken);
        }
    }
}
