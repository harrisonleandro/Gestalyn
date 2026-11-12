using System.Net;
using ProjectManager.Application.Planning;
using ProjectManager.Application.Tasks;
using ProjectManager.IntegrationTests.Infrastructure;

namespace ProjectManager.IntegrationTests.Tasks;

/// <summary>Fases e marcos do projeto: criação, edição, ordem, vínculos e exclusão (ADR-011).</summary>
public sealed class PlanningTests : IDisposable
{
    private readonly ApiFactory _factory;
    private readonly HttpClient _client;
    private readonly TaskTestKit _kit;

    public PlanningTests(PostgresDatabaseFixture database, MailpitFixture mailpit)
    {
        _factory = new ApiFactory(database.ConnectionString, mailpit);
        _client = _factory.CreateClient();
        _kit = new TaskTestKit(_client, mailpit);
    }

    [Fact]
    public async Task Phases_AreCreatedInOrder_EditedReordered_AndDeletedOnlyWithoutTasks()
    {
        var workspace = await _kit.CreateWorkspaceAsync();
        var discovery = await _kit.CreateInProjectAsync<PhaseDetails>(workspace, "/phases", new { name = "Descoberta", startDate = "2027-01-01", endDate = "2027-01-31" });
        var build = await _kit.CreateInProjectAsync<PhaseDetails>(workspace, "/phases", new { name = "Construção" });
        var launch = await _kit.CreateInProjectAsync<PhaseDetails>(workspace, "/phases", new { name = "Lançamento" });

        using var invalidDates = await _kit.SendToProjectAsync(HttpMethod.Patch, workspace, $"/phases/{discovery.Id}", workspace.Owner, new { endDate = "2026-12-31" });
        using var clearEnd = await _kit.SendToProjectAsync(HttpMethod.Patch, workspace, $"/phases/{discovery.Id}", workspace.Owner, new { endDate = (string?)null, description = "Entender o problema" });
        using var reorder = await _kit.SendToProjectAsync(HttpMethod.Put, workspace, "/phases/order", workspace.Owner, new { phaseIds = new List<Guid> { launch.Id, discovery.Id, build.Id } });
        using var incompleteOrder = await _kit.SendToProjectAsync(HttpMethod.Put, workspace, "/phases/order", workspace.Owner, new { phaseIds = new List<Guid> { launch.Id, discovery.Id } });

        Assert.Equal((1, 2, 3), (discovery.Position, build.Position, launch.Position));
        Assert.Equal(HttpStatusCode.BadRequest, invalidDates.StatusCode);
        var cleared = await TaskTestKit.ReadAsync<PhaseDetails>(clearEnd);
        Assert.Equal((new DateOnly(2027, 1, 1), (DateOnly?)null, "Entender o problema"), (cleared.StartDate, cleared.EndDate, cleared.Description));
        Assert.Equal(["Lançamento", "Descoberta", "Construção"], (await TaskTestKit.ReadAsync<List<PhaseDetails>>(reorder)).Select(p => p.Name));
        Assert.Equal(HttpStatusCode.BadRequest, incompleteOrder.StatusCode);

        var milestone = await _kit.CreateInProjectAsync<MilestoneDetails>(workspace, "/milestones", new { name = "Beta", phaseId = launch.Id });
        await _kit.CreateTaskAsync(workspace, workspace.Owner, new { title = "Na construção", phaseId = build.Id });

        using var deleteWithTasks = await _kit.SendToProjectAsync(HttpMethod.Delete, workspace, $"/phases/{build.Id}", workspace.Owner);
        using var deleteEmpty = await _kit.SendToProjectAsync(HttpMethod.Delete, workspace, $"/phases/{launch.Id}", workspace.Owner);
        using var phases = await _kit.SendToProjectAsync(HttpMethod.Get, workspace, "/phases", workspace.Owner);
        using var milestones = await _kit.SendToProjectAsync(HttpMethod.Get, workspace, "/milestones", workspace.Owner);

        Assert.Equal(HttpStatusCode.Conflict, deleteWithTasks.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, deleteEmpty.StatusCode);
        Assert.Equal(["Descoberta", "Construção"], (await TaskTestKit.ReadAsync<List<PhaseDetails>>(phases)).Select(p => p.Name));
        Assert.Null(Assert.Single(await TaskTestKit.ReadAsync<List<MilestoneDetails>>(milestones), m => m.Id == milestone.Id).PhaseId);
    }

    [Fact]
    public async Task Milestones_UsePhasesOfTheSameProject_TrackReaching_AndKeepTheirTasks()
    {
        var workspace = await _kit.CreateWorkspaceAsync();
        var otherProject = await _kit.CreateSecondProjectAsync(workspace);
        using var foreignPhaseResponse = await _kit.Projects.SendToProjectAsync(
            HttpMethod.Post, otherProject.Id, "/phases", workspace.Owner, workspace.OrganizationId, new { name = "Fase de outro projeto" });
        var foreignPhase = await TaskTestKit.ReadAsync<PhaseDetails>(foreignPhaseResponse);

        using var withForeignPhase = await _kit.SendToProjectAsync(HttpMethod.Post, workspace, "/milestones", workspace.Owner, new { name = "Errado", phaseId = foreignPhase.Id });
        var milestone = await _kit.CreateInProjectAsync<MilestoneDetails>(workspace, "/milestones", new { name = "Versão 1.0", dueDate = "2027-06-30" });

        using var reach = await _kit.SendToProjectAsync(HttpMethod.Patch, workspace, $"/milestones/{milestone.Id}", workspace.Owner, new { isReached = true });
        var reached = await TaskTestKit.ReadAsync<MilestoneDetails>(reach);
        using var reachAgain = await _kit.SendToProjectAsync(HttpMethod.Patch, workspace, $"/milestones/{milestone.Id}", workspace.Owner, new { isReached = true });
        using var unreach = await _kit.SendToProjectAsync(HttpMethod.Patch, workspace, $"/milestones/{milestone.Id}", workspace.Owner, new { isReached = false });

        Assert.Equal(HttpStatusCode.NotFound, withForeignPhase.StatusCode);
        Assert.NotNull(reached.ReachedAt);
        Assert.Equal(reached.ReachedAt, (await TaskTestKit.ReadAsync<MilestoneDetails>(reachAgain)).ReachedAt);
        Assert.Null((await TaskTestKit.ReadAsync<MilestoneDetails>(unreach)).ReachedAt);

        var task = await _kit.CreateTaskAsync(workspace, workspace.Owner, new { title = "Entrega", milestoneId = milestone.Id });
        using var deleteWithTask = await _kit.SendToProjectAsync(HttpMethod.Delete, workspace, $"/milestones/{milestone.Id}", workspace.Owner);
        using var unlink = await _kit.SendToTaskAsync(HttpMethod.Patch, workspace, task.Id, "", workspace.Owner, new { milestoneId = (Guid?)null });
        using var deleteAfterUnlink = await _kit.SendToProjectAsync(HttpMethod.Delete, workspace, $"/milestones/{milestone.Id}", workspace.Owner);

        Assert.Equal(HttpStatusCode.Conflict, deleteWithTask.StatusCode);
        Assert.Null((await TaskTestKit.ReadAsync<TaskDetails>(unlink)).MilestoneId);
        Assert.Equal(HttpStatusCode.NoContent, deleteAfterUnlink.StatusCode);
    }

    [Fact]
    public async Task OnlyManagersPlan_WhileTheWholeTeamReadsThePlan()
    {
        var workspace = await _kit.CreateWorkspaceAsync();
        var contributor = await _kit.AddTeamMemberAsync(workspace, "Contributor");
        var viewer = await _kit.AddTeamMemberAsync(workspace, "Viewer");
        await _kit.CreateInProjectAsync<PhaseDetails>(workspace, "/phases", new { name = "Fase" });

        foreach (var user in new[] { contributor, viewer })
        {
            using var read = await _kit.SendToProjectAsync(HttpMethod.Get, workspace, "/phases", user);
            using var readMilestones = await _kit.SendToProjectAsync(HttpMethod.Get, workspace, "/milestones", user);
            using var createPhase = await _kit.SendToProjectAsync(HttpMethod.Post, workspace, "/phases", user, new { name = "Não pode" });
            using var createMilestone = await _kit.SendToProjectAsync(HttpMethod.Post, workspace, "/milestones", user, new { name = "Não pode" });

            Assert.Equal(HttpStatusCode.OK, read.StatusCode);
            Assert.Equal(HttpStatusCode.OK, readMilestones.StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, createPhase.StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, createMilestone.StatusCode);
        }
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }
}
