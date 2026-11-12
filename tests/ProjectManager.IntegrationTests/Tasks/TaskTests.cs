using System.Net;
using ProjectManager.Application.Common;
using ProjectManager.Application.Planning;
using ProjectManager.Application.Tasks;
using ProjectManager.Domain.Common;
using ProjectManager.Domain.Projects;
using ProjectManager.IntegrationTests.Infrastructure;
using ProjectManager.IntegrationTests.Organizations;

namespace ProjectManager.IntegrationTests.Tasks;

/// <summary>Tarefas pela API: número, dados, responsáveis, arquivamento, filtros e ordenação.</summary>
public sealed class TaskTests : IDisposable
{
    private readonly ApiFactory _factory;
    private readonly HttpClient _client;
    private readonly TaskTestKit _kit;

    public TaskTests(PostgresDatabaseFixture database, MailpitFixture mailpit)
    {
        _factory = new ApiFactory(database.ConnectionString, mailpit);
        _client = _factory.CreateClient();
        _kit = new TaskTestKit(_client, mailpit);
    }

    [Fact]
    public async Task Create_NumbersTasksPerProject_WithPhaseMilestoneLabelsAndAssignees()
    {
        var workspace = await _kit.CreateWorkspaceAsync();
        var contributor = await _kit.AddTeamMemberAsync(workspace, "Contributor");
        var phase = await _kit.CreateInProjectAsync<PhaseDetails>(workspace, "/phases", new { name = "Construção" });
        var milestone = await _kit.CreateInProjectAsync<MilestoneDetails>(workspace, "/milestones", new { name = "Beta" });

        var first = await _kit.CreateTaskAsync(workspace, "Primeira");
        var second = await _kit.CreateTaskAsync(workspace, contributor, new
        {
            title = " Tela de login ",
            description = "Detalhes em **Markdown**",
            priority = "High",
            dueDate = "2027-02-15",
            phaseId = phase.Id,
            milestoneId = milestone.Id,
            labels = new List<string> { "Web", "web", "Segurança" },
            assigneeIds = new List<Guid> { contributor.Id, workspace.Owner.Id },
        });
        var otherProject = await _kit.CreateSecondProjectAsync(workspace);
        using var otherProjectTask = await _kit.Projects.SendToProjectAsync(
            HttpMethod.Post, otherProject.Id, "/tasks", workspace.Owner, workspace.OrganizationId, new { title = "No outro projeto" });

        Assert.Equal((1, 2), (first.Number, second.Number));
        Assert.Equal(1, (await TaskTestKit.ReadAsync<TaskDetails>(otherProjectTask)).Number);
        Assert.Equal(Priority.Medium, first.Priority);
        Assert.Equal("Tela de login", second.Title);
        Assert.Equal((Priority.High, new DateOnly(2027, 2, 15)), (second.Priority, second.DueDate));
        Assert.Equal((phase.Id, milestone.Id), (second.PhaseId, second.MilestoneId));
        Assert.Equal(["Segurança", "Web"], second.Labels);
        Assert.Equal(new[] { contributor.Id, workspace.Owner.Id }.Order(), second.Assignees.Select(a => a.UserId).Order());
        Assert.Equal(contributor.Id, second.CreatedByUserId);
        Assert.Contains(ProjectPermissions.TasksWrite, second.MyPermissions);
    }

    [Fact]
    public async Task ConcurrentCreation_NeverRepeatsNumbers()
    {
        var workspace = await _kit.CreateWorkspaceAsync();

        var responses = await Task.WhenAll(Enumerable.Range(1, 8).Select(i =>
            _kit.SendToProjectAsync(HttpMethod.Post, workspace, "/tasks", workspace.Owner, new { title = $"Simultânea {i}" })));

        var created = new List<TaskDetails>();
        foreach (var response in responses)
        {
            using (response)
            {
                if (response.StatusCode == HttpStatusCode.Created)
                {
                    created.Add(await TaskTestKit.ReadAsync<TaskDetails>(response));
                }
                else
                {
                    Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
                }
            }
        }

        Assert.NotEmpty(created);
        Assert.Equal(created.Count, created.Select(task => task.Number).Distinct().Count());
        Assert.Equal(Enumerable.Range(1, created.Count), created.Select(task => task.Number).Order());
    }

    [Fact]
    public async Task Update_ChangesOnlyTheSentFields_AndRejectsLinksFromAnotherProject()
    {
        var workspace = await _kit.CreateWorkspaceAsync();
        var phase = await _kit.CreateInProjectAsync<PhaseDetails>(workspace, "/phases", new { name = "Fase" });
        var task = await _kit.CreateTaskAsync(workspace, workspace.Owner, new { title = "Original", description = "Texto", dueDate = "2027-01-10", phaseId = phase.Id, labels = new List<string> { "a" } });
        var otherProject = await _kit.CreateSecondProjectAsync(workspace);
        using var foreignPhaseResponse = await _kit.Projects.SendToProjectAsync(
            HttpMethod.Post, otherProject.Id, "/phases", workspace.Owner, workspace.OrganizationId, new { name = "Fase alheia" });
        var foreignPhase = await TaskTestKit.ReadAsync<PhaseDetails>(foreignPhaseResponse);

        using var rename = await _kit.SendToTaskAsync(HttpMethod.Patch, workspace, task.Id, "", workspace.Owner, new { title = "Renomeada", priority = "Critical" });
        using var clear = await _kit.SendToTaskAsync(HttpMethod.Patch, workspace, task.Id, "", workspace.Owner, new { description = (string?)null, dueDate = (string?)null, phaseId = (Guid?)null, labels = new List<string>() });
        using var foreign = await _kit.SendToTaskAsync(HttpMethod.Patch, workspace, task.Id, "", workspace.Owner, new { phaseId = foreignPhase.Id });

        var renamed = await TaskTestKit.ReadAsync<TaskDetails>(rename);
        Assert.Equal(("Renomeada", Priority.Critical, "Texto", phase.Id), (renamed.Title, renamed.Priority, renamed.Description, renamed.PhaseId));
        var cleared = await TaskTestKit.ReadAsync<TaskDetails>(clear);
        Assert.Null(cleared.Description);
        Assert.Null(cleared.DueDate);
        Assert.Null(cleared.PhaseId);
        Assert.Empty(cleared.Labels);
        Assert.Equal("Renomeada", cleared.Title);
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
    }

    [Fact]
    public async Task Archive_MakesTheTaskReadOnly_AndArchivedProjectsBlockTasks()
    {
        var workspace = await _kit.CreateWorkspaceAsync();
        var task = await _kit.CreateTaskAsync(workspace, "Antiga");
        var active = await _kit.CreateTaskAsync(workspace, "Ativa");

        using var archive = await _kit.SendToTaskAsync(HttpMethod.Delete, workspace, task.Id, "", workspace.Owner);
        using var archiveAgain = await _kit.SendToTaskAsync(HttpMethod.Delete, workspace, task.Id, "", workspace.Owner);
        using var edit = await _kit.SendToTaskAsync(HttpMethod.Patch, workspace, task.Id, "", workspace.Owner, new { title = "Nova" });
        using var comment = await _kit.SendToTaskAsync(HttpMethod.Post, workspace, task.Id, "/comments", workspace.Owner, new { body = "Oi" });
        using var details = await _kit.SendToTaskAsync(HttpMethod.Get, workspace, task.Id, "", workspace.Owner);
        using var defaultList = await _kit.SendToProjectAsync(HttpMethod.Get, workspace, "/tasks", workspace.Owner);
        using var fullList = await _kit.SendToProjectAsync(HttpMethod.Get, workspace, "/tasks?includeArchived=true", workspace.Owner);

        Assert.Equal(HttpStatusCode.NoContent, archive.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, archiveAgain.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, edit.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, comment.StatusCode);
        var archived = await TaskTestKit.ReadAsync<TaskDetails>(details);
        Assert.Equal(("Antiga", workspace.Owner.Id), (archived.Title, archived.ArchivedByUserId));
        Assert.Equal([active.Id], (await TaskTestKit.ReadAsync<PagedResult<TaskSummary>>(defaultList)).Items.Select(t => t.Id));
        Assert.Equal(2, (await TaskTestKit.ReadAsync<PagedResult<TaskSummary>>(fullList)).TotalCount);

        using var archiveProject = await _kit.SendToProjectAsync(HttpMethod.Delete, workspace, "", workspace.Owner);
        using var createInArchived = await _kit.SendToProjectAsync(HttpMethod.Post, workspace, "/tasks", workspace.Owner, new { title = "Não pode" });
        using var editInArchived = await _kit.SendToTaskAsync(HttpMethod.Patch, workspace, active.Id, "", workspace.Owner, new { title = "Não pode" });
        using var phaseInArchived = await _kit.SendToProjectAsync(HttpMethod.Post, workspace, "/phases", workspace.Owner, new { name = "Não pode" });

        Assert.Equal(HttpStatusCode.NoContent, archiveProject.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, createInArchived.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, editInArchived.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, phaseInArchived.StatusCode);
    }

    [Fact]
    public async Task Assignees_MustBeOnTheTeam_AndLeaveTasksWhenTheyLeaveTheTeam()
    {
        var workspace = await _kit.CreateWorkspaceAsync();
        var contributor = await _kit.AddTeamMemberAsync(workspace, "Contributor");
        var outsideTheTeam = await _kit.Organizations.CreateUserAsync();
        await _kit.Organizations.AddMemberAsync(workspace.Owner, workspace.OrganizationId, outsideTheTeam, "Member");
        var task = await _kit.CreateTaskAsync(workspace, "Com responsáveis");

        using var assignOutsider = await _kit.SendToTaskAsync(HttpMethod.Post, workspace, task.Id, "/assignments", workspace.Owner, new { userId = outsideTheTeam.Id });
        using var createWithOutsider = await _kit.SendToProjectAsync(HttpMethod.Post, workspace, "/tasks", workspace.Owner, new { title = "x", assigneeIds = new List<Guid> { outsideTheTeam.Id } });
        using var assign = await _kit.SendToTaskAsync(HttpMethod.Post, workspace, task.Id, "/assignments", workspace.Owner, new { userId = contributor.Id });
        using var assignAgain = await _kit.SendToTaskAsync(HttpMethod.Post, workspace, task.Id, "/assignments", workspace.Owner, new { userId = contributor.Id });

        Assert.Equal(HttpStatusCode.NotFound, assignOutsider.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, createWithOutsider.StatusCode);
        Assert.Equal([contributor.Id], (await TaskTestKit.ReadAsync<TaskDetails>(assign)).Assignees.Select(a => a.UserId));
        Assert.Equal(HttpStatusCode.Conflict, assignAgain.StatusCode);

        using var leaveTeam = await _kit.SendToProjectAsync(HttpMethod.Delete, workspace, $"/members/{contributor.Id}", workspace.Owner);
        using var afterLeaving = await _kit.SendToTaskAsync(HttpMethod.Get, workspace, task.Id, "", workspace.Owner);
        using var unassignMissing = await _kit.SendToTaskAsync(HttpMethod.Delete, workspace, task.Id, $"/assignments/{contributor.Id}", workspace.Owner);

        Assert.Equal(HttpStatusCode.NoContent, leaveTeam.StatusCode);
        Assert.Empty((await TaskTestKit.ReadAsync<TaskDetails>(afterLeaving)).Assignees);
        Assert.Equal(HttpStatusCode.NotFound, unassignMissing.StatusCode);
    }

    [Fact]
    public async Task List_FiltersSortsAndPaginates()
    {
        var workspace = await _kit.CreateWorkspaceAsync();
        var contributor = await _kit.AddTeamMemberAsync(workspace, "Contributor");
        var phase = await _kit.CreateInProjectAsync<PhaseDetails>(workspace, "/phases", new { name = "Fase" });
        var milestone = await _kit.CreateInProjectAsync<MilestoneDetails>(workspace, "/milestones", new { name = "Marco" });
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var overdue = await _kit.CreateTaskAsync(workspace, workspace.Owner, new { title = "Atrasada 100%", priority = "Low", dueDate = today.AddDays(-3), labels = new List<string> { "Bug" } });
        var critical = await _kit.CreateTaskAsync(workspace, workspace.Owner, new { title = "Crítica", priority = "Critical", dueDate = today.AddDays(10), phaseId = phase.Id, assigneeIds = new List<Guid> { contributor.Id } });
        var noDate = await _kit.CreateTaskAsync(workspace, workspace.Owner, new { title = "Sem prazo", priority = "High", milestoneId = milestone.Id, labels = new List<string> { "bug" } });

        async Task<List<int>> NumbersAsync(string query, TestUser? user = null)
        {
            using var response = await _kit.SendToProjectAsync(HttpMethod.Get, workspace, $"/tasks{query}", user ?? workspace.Owner);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return (await TaskTestKit.ReadAsync<PagedResult<TaskSummary>>(response)).Items.Select(t => t.Number).ToList();
        }

        Assert.Equal([critical.Number], await NumbersAsync($"?phaseId={phase.Id}"));
        Assert.Equal([noDate.Number], await NumbersAsync($"?milestoneId={milestone.Id}"));
        Assert.Equal([critical.Number], await NumbersAsync($"?assigneeId={contributor.Id}"));
        Assert.Equal([critical.Number], await NumbersAsync("?assignedToMe=true", contributor));
        Assert.Empty(await NumbersAsync("?assignedToMe=true"));
        Assert.Equal([overdue.Number, noDate.Number], await NumbersAsync("?label=BUG"));
        Assert.Equal([noDate.Number], await NumbersAsync("?priority=High"));
        Assert.Equal([overdue.Number], await NumbersAsync("?overdue=true"));
        Assert.Equal([overdue.Number], await NumbersAsync($"?dueBefore={today:O}"));
        Assert.Equal([critical.Number], await NumbersAsync($"?dueAfter={today:O}"));
        Assert.Equal([overdue.Number], await NumbersAsync("?search=100%25"));
        Assert.Empty(await NumbersAsync("?search=_"));
        Assert.Equal([critical.Number], await NumbersAsync("?search=CRÍT"));
        Assert.Equal([critical.Number, noDate.Number, overdue.Number], await NumbersAsync("?sortBy=Priority&sortDirection=Desc"));
        Assert.Equal([overdue.Number, critical.Number, noDate.Number], await NumbersAsync("?sortBy=DueDate"));
        Assert.Equal([noDate.Number, critical.Number, overdue.Number], await NumbersAsync("?sortDirection=Desc"));

        using var page = await _kit.SendToProjectAsync(HttpMethod.Get, workspace, "/tasks?pageSize=2&page=2", workspace.Owner);
        var second = await TaskTestKit.ReadAsync<PagedResult<TaskSummary>>(page);
        Assert.Equal((3, 2, 2), (second.TotalCount, second.Page, second.PageSize));
        Assert.Equal([noDate.Number], second.Items.Select(t => t.Number));

        using var invalidSort = await _kit.SendToProjectAsync(HttpMethod.Get, workspace, "/tasks?sortBy=Alfabetico", workspace.Owner);
        Assert.Equal(HttpStatusCode.BadRequest, invalidSort.StatusCode);
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }
}
