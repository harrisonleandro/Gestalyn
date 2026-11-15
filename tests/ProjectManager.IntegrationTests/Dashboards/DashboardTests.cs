using System.Net;
using Npgsql;
using ProjectManager.Application.Dashboards;
using ProjectManager.Application.Planning;
using ProjectManager.Application.Tasks;
using ProjectManager.Application.Workflows;
using ProjectManager.Domain.Projects;
using ProjectManager.IntegrationTests.Infrastructure;
using ProjectManager.IntegrationTests.Projects;
using ProjectManager.IntegrationTests.Tasks;

namespace ProjectManager.IntegrationTests.Dashboards;

/// <summary>
/// Progresso, dashboards e estatísticas (ADR-014, D7 e D8) conferidos contra dados montados com valores conhecidos.
/// </summary>
public sealed class DashboardTests : IDisposable
{
    private readonly PostgresDatabaseFixture _database;
    private readonly ApiFactory _factory;
    private readonly TaskTestKit _kit;

    public DashboardTests(PostgresDatabaseFixture database, MailpitFixture mailpit)
    {
        _database = database;
        _factory = new ApiFactory(database.ConnectionString, mailpit);
        _kit = new TaskTestKit(_factory.CreateClient(), mailpit);
    }

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    [Fact]
    public async Task Progress_FollowsTheDocumentedRule_ForTheProjectPhasesAndMilestones()
    {
        var scenario = await BuildScenarioAsync();

        var progress = await ReadAsync<ProjectProgress>(scenario.Workspace, $"/projects/{scenario.Workspace.ProjectId}/progress");

        // Sem a arquivada: 2 concluídas, 1 cancelada, 1 bloqueada, 1 em andamento e 1 a fazer → 2 ÷ (6 − 1).
        Assert.Equal(new CategoryCounts(1, 1, 1, 2, 1), progress.Project.Counts);
        Assert.Equal(40.0, progress.Project.Percent);
        Assert.Equal("doneOverNotCanceled", progress.Project.Rule);

        var withTasks = progress.Phases.Single(phase => phase.PhaseId == scenario.Phase.Id);
        var empty = progress.Phases.Single(phase => phase.PhaseId != scenario.Phase.Id);
        Assert.Equal((new CategoryCounts(1, 0, 0, 2, 0), 66.7), (withTasks.Progress.Counts, withTasks.Progress.Percent));
        Assert.Null(empty.Progress.Percent);
        var milestone = Assert.Single(progress.Milestones);
        Assert.Equal((new CategoryCounts(0, 0, 1, 1, 1), 50.0), (milestone.Progress.Counts, milestone.Progress.Percent));
    }

    [Fact]
    public async Task ProjectDashboard_ShowsRealNumbers()
    {
        var scenario = await BuildScenarioAsync();

        var dashboard = await ReadAsync<ProjectDashboard>(scenario.Workspace, $"/projects/{scenario.Workspace.ProjectId}/dashboard");

        Assert.Equal(Today, dashboard.Today);
        Assert.Equal(40.0, dashboard.Progress.Percent);
        Assert.Equal(["Backlog", "A fazer", "Em andamento", "Em revisão", "Bloqueada", "Concluída", "Cancelada"], dashboard.ByState.Select(s => s.Name));
        Assert.Equal([1, 0, 1, 0, 1, 2, 1], dashboard.ByState.Select(s => s.Count));
        Assert.Equal((1, scenario.Overdue.Id), (dashboard.OverdueCount, Assert.Single(dashboard.Overdue).Id));
        Assert.Equal((1, scenario.DueSoon.Id), (dashboard.DueSoonCount, Assert.Single(dashboard.DueSoon).Id));
        Assert.Equal((1, "Bloqueada"), (dashboard.BlockedCount, Assert.Single(dashboard.Blocked).StateName));
        Assert.Equal(50.0, Assert.Single(dashboard.UpcomingMilestones).Progress.Percent);
        Assert.Equal(10, dashboard.RecentActivity.Count);
        Assert.True(dashboard.RecentActivity.Zip(dashboard.RecentActivity.Skip(1)).All(pair => pair.First.OccurredAt >= pair.Second.OccurredAt));
    }

    [Fact]
    public async Task Statistics_CountCreatedAndCompletedTasksPerWeek_AndTheCycleTime()
    {
        var scenario = await BuildScenarioAsync();

        // As três conclusões (inclusive a da tarefa arquivada depois) ganham 1, 2 e 4 dias desde a criação.
        var daysByTask = scenario.Completed.Zip([1, 2, 4]).ToList();
        foreach (var (taskId, days) in daysByTask)
        {
            await ExecuteAsync("UPDATE tasks SET created_at = now() - make_interval(days => @days) WHERE id = @task", ("days", days), ("task", taskId));
        }

        var statistics = await ReadAsync<ProjectStatistics>(scenario.Workspace, $"/projects/{scenario.Workspace.ProjectId}/statistics?weeks=4");
        using var tooMany = await _kit.SendToProjectAsync(HttpMethod.Get, scenario.Workspace, "/statistics?weeks=27", scenario.Workspace.Owner);
        using var none = await _kit.SendToProjectAsync(HttpMethod.Get, scenario.Workspace, "/statistics?weeks=0", scenario.Workspace.Owner);

        Assert.Equal(4, statistics.Weekly.Count);
        Assert.Equal(WeekStart(Today), statistics.Weekly[^1].WeekStart);
        Assert.Equal(WeekStart(Today).AddDays(-21), statistics.From);
        Assert.Equal(7, statistics.Weekly.Sum(week => week.Created));
        Assert.Equal(3, statistics.Weekly[^1].Completed);
        Assert.Equal(new CycleTime(3, 2.0, 4.0), statistics.CycleTime);
        Assert.Equal((HttpStatusCode.BadRequest, HttpStatusCode.BadRequest), (tooMany.StatusCode, none.StatusCode));
    }

    [Fact]
    public async Task OrganizationDashboard_CountsOnlyTheProjectsTheUserSees()
    {
        var scenario = await BuildScenarioAsync();
        var hidden = await _kit.CreateSecondProjectAsync(scenario.Workspace, "Projeto restrito");
        await _kit.CreateTaskAsync(new Workspace(scenario.Workspace.Owner, scenario.Workspace.OrganizationId, hidden.Id), "Invisível para a equipe");

        var forOwner = await ReadOrganizationAsync(scenario.Workspace, scenario.Workspace.Owner);
        var forContributor = await ReadOrganizationAsync(scenario.Workspace, scenario.Contributor);
        var outsider = await _kit.CreateWorkspaceAsync("Outra organização");
        using var asOutsider = await _kit.Projects.SendAsync(HttpMethod.Get, $"/api/v1/organizations/{scenario.Workspace.OrganizationId}/dashboard", outsider.Owner);
        using var projectAsOutsider = await _kit.Projects.SendToProjectAsync(
            HttpMethod.Get, scenario.Workspace.ProjectId, "/dashboard", outsider.Owner, outsider.OrganizationId);

        Assert.Equal(2, forOwner.ProjectCount);
        Assert.Equal(4, forOwner.OpenCount);
        Assert.Equal(1, forOwner.OverdueCount);
        Assert.Equal(3, forOwner.CompletedLast7Days);
        Assert.Equal(["Portal", "Projeto restrito"], forOwner.Projects.Select(p => p.Name));

        Assert.Equal(scenario.Workspace.ProjectId, Assert.Single(forContributor.Projects).ProjectId);
        Assert.Equal((3, 1, 3), (forContributor.OpenCount, forContributor.OverdueCount, forContributor.CompletedLast7Days));
        Assert.Equal((1, scenario.Overdue.Id), (forContributor.MyOpenCount, Assert.Single(forContributor.MyOpenTasks).Id));
        Assert.Equal(0, forOwner.MyOpenCount);
        Assert.Equal((HttpStatusCode.NotFound, HttpStatusCode.NotFound), (asOutsider.StatusCode, projectAsOutsider.StatusCode));
    }

    public void Dispose() => _factory.Dispose();

    private static DateOnly WeekStart(DateOnly date) => date.AddDays(-(((int)date.DayOfWeek + 6) % 7));

    /// <summary>
    /// Projeto "Portal" com duas fases, um marco, um estado cancelado e sete tarefas: duas concluídas e uma concluída e
    /// arquivada (na fase), uma a fazer com prazo vencido (na fase, da colaboradora), uma em andamento com prazo em
    /// três dias, uma bloqueada e uma cancelada (estas três no marco, com uma das concluídas).
    /// </summary>
    private async Task<Scenario> BuildScenarioAsync()
    {
        var workspace = await _kit.CreateWorkspaceAsync("Portal");
        var contributor = await _kit.AddTeamMemberAsync(workspace, "Contributor");
        var phase = await _kit.CreateInProjectAsync<PhaseDetails>(workspace, "/phases", new { name = "Construção" });
        await _kit.CreateInProjectAsync<PhaseDetails>(workspace, "/phases", new { name = "Sem tarefas" });
        var milestone = await _kit.CreateInProjectAsync<MilestoneDetails>(workspace, "/milestones", new { name = "Lançamento", dueDate = Today.AddDays(30) });
        var canceled = await _kit.CreateInProjectAsync<WorkflowStateDetails>(workspace, "/workflow/states", new { name = "Cancelada", category = "Canceled" });
        var workflow = await ReadAsync<WorkflowDetails>(workspace, $"/projects/{workspace.ProjectId}/workflow");
        var backlog = workflow.States.Single(s => s.Name == "Backlog").Id;
        using (var toCanceled = await _kit.Projects.SendAsync(HttpMethod.Post, "/api/v1/workflow/transitions", workspace.Owner,
            new { fromStateId = backlog, toStateId = canceled.Id }, workspace.OrganizationId.ToString()))
        {
            toCanceled.EnsureSuccessStatusCode();
        }

        var doneA = await NewTaskAsync(workspace, new { title = "Concluída A", phaseId = phase.Id });
        var doneB = await NewTaskAsync(workspace, new { title = "Concluída B", phaseId = phase.Id, milestoneId = milestone.Id });
        var archived = await NewTaskAsync(workspace, new { title = "Concluída e arquivada", phaseId = phase.Id });
        var overdue = await NewTaskAsync(workspace, new { title = "Atrasada", phaseId = phase.Id, dueDate = Today.AddDays(-1), assigneeIds = new[] { contributor.Id } });
        var dueSoon = await NewTaskAsync(workspace, new { title = "Vence logo", dueDate = Today.AddDays(3) });
        var blocked = await NewTaskAsync(workspace, new { title = "Bloqueada", milestoneId = milestone.Id });
        var canceledTask = await NewTaskAsync(workspace, new { title = "Cancelada", milestoneId = milestone.Id });

        foreach (var task in new[] { doneA, doneB, archived })
        {
            await MoveAsync(workspace, workflow, task, "A fazer", "Em andamento", "Em revisão", "Concluída");
        }

        await MoveAsync(workspace, workflow, dueSoon, "A fazer", "Em andamento");
        await MoveAsync(workspace, workflow, blocked, "Bloqueada");
        await MoveAsync(workspace, workflow, canceledTask, "Cancelada");
        using (var archive = await _kit.SendToTaskAsync(HttpMethod.Delete, workspace, archived.Id, "", workspace.Owner))
        {
            archive.EnsureSuccessStatusCode();
        }

        return new Scenario(workspace, contributor, phase, overdue, dueSoon, [doneA.Id, doneB.Id, archived.Id]);
    }

    private Task<TaskDetails> NewTaskAsync(Workspace workspace, object body) => _kit.CreateTaskAsync(workspace, workspace.Owner, body);

    private async Task MoveAsync(Workspace workspace, WorkflowDetails workflow, TaskDetails task, params string[] states)
    {
        foreach (var state in states)
        {
            var target = workflow.States.Single(s => s.Name == state).Id;
            using var response = await _kit.SendToTaskAsync(HttpMethod.Post, workspace, task.Id, "/transitions", workspace.Owner, new { toStateId = target });
            Assert.True(response.IsSuccessStatusCode, $"{task.Title} → {state}: {(int)response.StatusCode}");
        }
    }

    private async Task<T> ReadAsync<T>(Workspace workspace, string path)
    {
        using var response = await _kit.Projects.SendAsync(HttpMethod.Get, $"/api/v1{path}", workspace.Owner, null, workspace.OrganizationId.ToString());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ProjectTestKit.ReadAsync<T>(response);
    }

    private async Task<OrganizationDashboard> ReadOrganizationAsync(Workspace workspace, Organizations.TestUser user)
    {
        using var response = await _kit.Projects.SendAsync(HttpMethod.Get, $"/api/v1/organizations/{workspace.OrganizationId}/dashboard", user);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ProjectTestKit.ReadAsync<OrganizationDashboard>(response);
    }

    private async Task ExecuteAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(_database.ConnectionString);
        await connection.OpenAsync(CancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await command.ExecuteNonQueryAsync(CancellationToken);
    }

    private sealed record Scenario(
        Workspace Workspace,
        Organizations.TestUser Contributor,
        PhaseDetails Phase,
        TaskDetails Overdue,
        TaskDetails DueSoon,
        IReadOnlyList<Guid> Completed);
}
