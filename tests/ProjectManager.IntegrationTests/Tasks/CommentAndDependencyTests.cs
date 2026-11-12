using System.Net;
using ProjectManager.Application.Tasks;
using ProjectManager.IntegrationTests.Infrastructure;

namespace ProjectManager.IntegrationTests.Tasks;

/// <summary>Comentários (regras de autoria) e dependências (mesmo projeto, sem ciclos).</summary>
public sealed class CommentAndDependencyTests : IDisposable
{
    private readonly ApiFactory _factory;
    private readonly HttpClient _client;
    private readonly TaskTestKit _kit;

    public CommentAndDependencyTests(PostgresDatabaseFixture database, MailpitFixture mailpit)
    {
        _factory = new ApiFactory(database.ConnectionString, mailpit);
        _client = _factory.CreateClient();
        _kit = new TaskTestKit(_client, mailpit);
    }

    [Fact]
    public async Task Comments_AreEditedOnlyByTheAuthor_AndDeletedByTheAuthorOrAManager()
    {
        var workspace = await _kit.CreateWorkspaceAsync();
        var author = await _kit.AddTeamMemberAsync(workspace, "Contributor");
        var colleague = await _kit.AddTeamMemberAsync(workspace, "Contributor");
        var viewer = await _kit.AddTeamMemberAsync(workspace, "Viewer");
        var task = await _kit.CreateTaskAsync(workspace, "Com conversa");

        using var create = await _kit.SendToTaskAsync(HttpMethod.Post, workspace, task.Id, "/comments", author, new { body = "Primeira versão" });
        var comment = await TaskTestKit.ReadAsync<CommentDetails>(create);
        using var viewerComments = await _kit.SendToTaskAsync(HttpMethod.Post, workspace, task.Id, "/comments", viewer, new { body = "Observador" });
        using var viewerReads = await _kit.SendToTaskAsync(HttpMethod.Get, workspace, task.Id, "/comments", viewer);
        using var colleagueEdits = await _kit.SendToTaskAsync(HttpMethod.Patch, workspace, task.Id, $"/comments/{comment.Id}", colleague, new { body = "Alterado por outro" });
        using var colleagueDeletes = await _kit.SendToTaskAsync(HttpMethod.Delete, workspace, task.Id, $"/comments/{comment.Id}", colleague);
        using var authorEdits = await _kit.SendToTaskAsync(HttpMethod.Patch, workspace, task.Id, $"/comments/{comment.Id}", author, new { body = "Versão revisada" });
        using var blank = await _kit.SendToTaskAsync(HttpMethod.Post, workspace, task.Id, "/comments", author, new { body = "   " });

        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        Assert.Equal((author.Id, "Primeira versão", (DateTimeOffset?)null), (comment.AuthorUserId, comment.Body, comment.EditedAt));
        Assert.Equal(HttpStatusCode.Forbidden, viewerComments.StatusCode);
        Assert.Single(await TaskTestKit.ReadAsync<List<CommentDetails>>(viewerReads));
        Assert.Equal(HttpStatusCode.Forbidden, colleagueEdits.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, colleagueDeletes.StatusCode);
        var edited = await TaskTestKit.ReadAsync<CommentDetails>(authorEdits);
        Assert.Equal("Versão revisada", edited.Body);
        Assert.NotNull(edited.EditedAt);
        Assert.Equal(HttpStatusCode.BadRequest, blank.StatusCode);

        using var colleagueComment = await _kit.SendToTaskAsync(HttpMethod.Post, workspace, task.Id, "/comments", colleague, new { body = "Do colega" });
        var second = await TaskTestKit.ReadAsync<CommentDetails>(colleagueComment);
        using var managerDeletes = await _kit.SendToTaskAsync(HttpMethod.Delete, workspace, task.Id, $"/comments/{second.Id}", workspace.Owner);
        using var authorDeletes = await _kit.SendToTaskAsync(HttpMethod.Delete, workspace, task.Id, $"/comments/{comment.Id}", author);
        using var remaining = await _kit.SendToTaskAsync(HttpMethod.Get, workspace, task.Id, "/comments", workspace.Owner);

        Assert.Equal(HttpStatusCode.NoContent, managerDeletes.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, authorDeletes.StatusCode);
        Assert.Empty(await TaskTestKit.ReadAsync<List<CommentDetails>>(remaining));
    }

    [Fact]
    public async Task Dependencies_RejectSelfDuplicatesOtherProjectsAndCycles()
    {
        var workspace = await _kit.CreateWorkspaceAsync();
        var a = await _kit.CreateTaskAsync(workspace, "A");
        var b = await _kit.CreateTaskAsync(workspace, "B");
        var c = await _kit.CreateTaskAsync(workspace, "C");
        var d = await _kit.CreateTaskAsync(workspace, "D");
        var e = await _kit.CreateTaskAsync(workspace, "E");
        var otherProject = await _kit.CreateSecondProjectAsync(workspace);
        using var foreignResponse = await _kit.Projects.SendToProjectAsync(
            HttpMethod.Post, otherProject.Id, "/tasks", workspace.Owner, workspace.OrganizationId, new { title = "Outro projeto" });
        var foreign = await TaskTestKit.ReadAsync<TaskDetails>(foreignResponse);

        // A depende de B, B de C, C de D: qualquer dependência de volta para A, B ou C fecha um ciclo.
        using var aOnB = await DependAsync(workspace, a.Id, b.Id);
        using var bOnC = await DependAsync(workspace, b.Id, c.Id);
        using var cOnD = await DependAsync(workspace, c.Id, d.Id);
        using var dOnA = await DependAsync(workspace, d.Id, a.Id);
        using var cOnA = await DependAsync(workspace, c.Id, a.Id);
        using var self = await DependAsync(workspace, a.Id, a.Id);
        using var duplicate = await DependAsync(workspace, a.Id, b.Id);
        using var acrossProjects = await DependAsync(workspace, a.Id, foreign.Id);
        using var aOnD = await DependAsync(workspace, a.Id, d.Id);

        Assert.Equal(HttpStatusCode.OK, aOnB.StatusCode);
        Assert.Equal(HttpStatusCode.OK, bOnC.StatusCode);
        Assert.Equal(HttpStatusCode.OK, cOnD.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, dOnA.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, cOnA.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, self.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, acrossProjects.StatusCode);
        Assert.Equal(HttpStatusCode.OK, aOnD.StatusCode);

        using var bDetails = await _kit.SendToTaskAsync(HttpMethod.Get, workspace, b.Id, "", workspace.Owner);
        var bTask = await TaskTestKit.ReadAsync<TaskDetails>(bDetails);
        Assert.Equal([c.Number], bTask.DependsOn.Select(t => t.Number));
        Assert.Equal([a.Number], bTask.Blocks.Select(t => t.Number));

        using var remove = await _kit.SendToTaskAsync(HttpMethod.Delete, workspace, b.Id, $"/dependencies/{c.Id}", workspace.Owner);
        using var removeAgain = await _kit.SendToTaskAsync(HttpMethod.Delete, workspace, b.Id, $"/dependencies/{c.Id}", workspace.Owner);
        using var dOnBAfterRemoval = await DependAsync(workspace, d.Id, b.Id);

        Assert.Equal(HttpStatusCode.NoContent, remove.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, removeAgain.StatusCode);
        Assert.Equal(HttpStatusCode.OK, dOnBAfterRemoval.StatusCode);

        using var archiveD = await _kit.SendToTaskAsync(HttpMethod.Delete, workspace, d.Id, "", workspace.Owner);
        using var onArchived = await DependAsync(workspace, e.Id, d.Id);
        Assert.Equal(HttpStatusCode.NoContent, archiveD.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, onArchived.StatusCode);
    }

    private Task<HttpResponseMessage> DependAsync(Workspace workspace, Guid taskId, Guid dependsOnTaskId) =>
        _kit.SendToTaskAsync(HttpMethod.Post, workspace, taskId, "/dependencies", workspace.Owner, new { dependsOnTaskId });

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }
}
