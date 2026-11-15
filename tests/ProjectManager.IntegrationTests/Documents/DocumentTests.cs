using System.Net;
using Npgsql;
using ProjectManager.Application.Documents;
using ProjectManager.Domain.Documents;
using ProjectManager.IntegrationTests.Infrastructure;
using ProjectManager.IntegrationTests.Organizations;
using ProjectManager.IntegrationTests.Projects;
using ProjectManager.IntegrationTests.Tasks;

namespace ProjectManager.IntegrationTests.Documents;

/// <summary>Documentos com versões e decisões (ADR-014, D5 e D6): conflitos, permissões, estados e isolamento.</summary>
public sealed class DocumentTests : IDisposable
{
    private readonly PostgresDatabaseFixture _database;
    private readonly ApiFactory _factory;
    private readonly TaskTestKit _kit;

    public DocumentTests(PostgresDatabaseFixture database, MailpitFixture mailpit)
    {
        _database = database;
        _factory = new ApiFactory(database.ConnectionString, mailpit);
        _kit = new TaskTestKit(_factory.CreateClient(), mailpit);
    }

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Editing_CreatesVersions_AndAnOutdatedVersionIsAConflict()
    {
        var workspace = await _kit.CreateWorkspaceAsync();
        const string markdown = "# Visão\n\n<script>alert(1)</script> **guardado como veio**";
        var created = await _kit.CreateInProjectAsync<DocumentDetails>(workspace, "/documents", new { title = "Visão", body = markdown });
        var path = $"/documents/{created.Id}";

        using var edit = await SendAsync(HttpMethod.Put, workspace, path, workspace.Owner, new { title = "Visão do produto", body = "# Versão 2", expectedVersion = 1 });
        using var outdated = await SendAsync(HttpMethod.Put, workspace, path, workspace.Owner, new { title = "Outra", body = "Sobrescreveria", expectedVersion = 1 });
        using var withoutVersion = await SendAsync(HttpMethod.Put, workspace, path, workspace.Owner, new { title = "Sem versão", body = "x" });

        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.Conflict, HttpStatusCode.BadRequest), (edit.StatusCode, outdated.StatusCode, withoutVersion.StatusCode));
        var current = await ReadAsync<DocumentDetails>(HttpMethod.Get, workspace, path);
        Assert.Equal(("Visão do produto", "# Versão 2", 2), (current.Title, current.Body, current.CurrentVersion));

        // Duas edições simultâneas a partir da mesma versão: só uma vence.
        var racing = await Task.WhenAll(Enumerable.Range(1, 2).Select(i =>
            SendAsync(HttpMethod.Put, workspace, path, workspace.Owner, new { title = $"Paralela {i}", body = $"Corpo {i}", expectedVersion = 2 })));
        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.Conflict], racing.Select(r => r.StatusCode).Order());
        Array.ForEach(racing, response => response.Dispose());

        var versions = await ReadAsync<List<DocumentVersionSummary>>(HttpMethod.Get, workspace, $"{path}/versions");
        var first = await ReadAsync<DocumentVersionDetails>(HttpMethod.Get, workspace, $"{path}/versions/1");
        using var missing = await SendAsync(HttpMethod.Get, workspace, $"{path}/versions/9", workspace.Owner);

        Assert.Equal([3, 2, 1], versions.Select(v => v.Number));
        Assert.All(versions, v => Assert.Equal(workspace.Owner.Id, v.AuthorId));
        Assert.Equal(("Visão", markdown), (first.Title, first.Body));
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task Documents_FollowProjectPermissions_AndArchivingMakesThemReadOnly()
    {
        var workspace = await _kit.CreateWorkspaceAsync();
        var contributor = await _kit.AddTeamMemberAsync(workspace, "Contributor");
        var viewer = await _kit.AddTeamMemberAsync(workspace, "Viewer");
        var outsider = await _kit.CreateWorkspaceAsync("Outra organização");

        using var byViewer = await SendAsync(HttpMethod.Post, workspace, $"/projects/{workspace.ProjectId}/documents", viewer, new { title = "Observador" });
        using var byContributor = await SendAsync(HttpMethod.Post, workspace, $"/projects/{workspace.ProjectId}/documents", contributor, new { title = "Guia", body = "Texto" });
        var document = await ProjectTestKit.ReadAsync<DocumentDetails>(byContributor);
        var path = $"/documents/{document.Id}";
        using var viewerReads = await SendAsync(HttpMethod.Get, workspace, path, viewer);
        using var contributorArchives = await SendAsync(HttpMethod.Post, workspace, $"{path}/archive", contributor);
        using var outsiderReads = await _kit.Projects.SendAsync(HttpMethod.Get, $"/api/v1{path}", outsider.Owner, null, outsider.OrganizationId.ToString());
        using var managerArchives = await SendAsync(HttpMethod.Post, workspace, $"{path}/archive", workspace.Owner);
        using var editArchived = await SendAsync(HttpMethod.Put, workspace, path, contributor, new { title = "Guia", body = "Mudou", expectedVersion = 1 });
        var active = await ReadAsync<List<DocumentSummary>>(HttpMethod.Get, workspace, $"/projects/{workspace.ProjectId}/documents");
        var withArchived = await ReadAsync<List<DocumentSummary>>(HttpMethod.Get, workspace, $"/projects/{workspace.ProjectId}/documents?includeArchived=true");
        using var restore = await SendAsync(HttpMethod.Post, workspace, $"{path}/restore", workspace.Owner);
        using var editRestored = await SendAsync(HttpMethod.Put, workspace, path, contributor, new { title = "Guia", body = "Mudou", expectedVersion = 1 });

        Assert.Equal(
            [HttpStatusCode.Forbidden, HttpStatusCode.Created, HttpStatusCode.OK, HttpStatusCode.Forbidden, HttpStatusCode.NotFound, HttpStatusCode.OK, HttpStatusCode.Conflict, HttpStatusCode.OK, HttpStatusCode.OK],
            new[]
            {
                byViewer.StatusCode, byContributor.StatusCode, viewerReads.StatusCode, contributorArchives.StatusCode, outsiderReads.StatusCode,
                managerArchives.StatusCode, editArchived.StatusCode, restore.StatusCode, editRestored.StatusCode,
            });
        Assert.Empty(active);
        Assert.Equal(document.Id, Assert.Single(withArchived).Id);
    }

    [Fact]
    public async Task Decisions_MoveFromProposedToDecided_AndAreImmutableAfterwards()
    {
        var workspace = await _kit.CreateWorkspaceAsync();
        var contributor = await _kit.AddTeamMemberAsync(workspace, "Contributor");
        var task = await _kit.CreateTaskAsync(workspace, "Escolher o banco");
        var otherProject = await _kit.CreateSecondProjectAsync(workspace);
        var foreignTask = await _kit.CreateTaskAsync(new Workspace(workspace.Owner, workspace.OrganizationId, otherProject.Id), "Fora");
        var decisions = $"/projects/{workspace.ProjectId}/decisions";

        using var withForeignTask = await SendAsync(HttpMethod.Post, workspace, decisions, contributor,
            new { title = "Banco", context = "Contexto", outcome = "PostgreSQL", taskId = foreignTask.Id });
        using var withoutOutcome = await SendAsync(HttpMethod.Post, workspace, decisions, contributor, new { title = "Banco", context = "Contexto" });
        using var proposed = await SendAsync(HttpMethod.Post, workspace, decisions, contributor,
            new { title = "Banco", context = "Precisamos persistir", outcome = "PostgreSQL", alternatives = "SQLite", taskId = task.Id });
        var decision = await ProjectTestKit.ReadAsync<DecisionDetails>(proposed);
        var path = $"/decisions/{decision.Id}";

        using var edit = await SendAsync(HttpMethod.Patch, workspace, path, contributor, new { consequences = "Operar o banco", alternatives = (string?)null });
        using var acceptByContributor = await SendAsync(HttpMethod.Post, workspace, $"{path}/accept", contributor);
        using var accept = await SendAsync(HttpMethod.Post, workspace, $"{path}/accept", workspace.Owner);
        using var editAccepted = await SendAsync(HttpMethod.Patch, workspace, path, contributor, new { title = "Mudou" });
        using var rejectAccepted = await SendAsync(HttpMethod.Post, workspace, $"{path}/reject", workspace.Owner);

        var proposal = await _kit.CreateInProjectAsync<DecisionDetails>(workspace, "/decisions", new { title = "Banco gerenciado", context = "Escala", outcome = "Serviço gerenciado" });
        using var supersedeByProposal = await SendAsync(HttpMethod.Post, workspace, $"{path}/supersede", workspace.Owner, new { supersededById = proposal.Id });
        using var supersedeBySelf = await SendAsync(HttpMethod.Post, workspace, $"{path}/supersede", workspace.Owner, new { supersededById = decision.Id });
        var foreign = await CreateDecisionInAsync(workspace, otherProject.Id);
        using var supersedeByForeign = await SendAsync(HttpMethod.Post, workspace, $"{path}/supersede", workspace.Owner, new { supersededById = foreign.Id });
        using (var acceptProposal = await SendAsync(HttpMethod.Post, workspace, $"/decisions/{proposal.Id}/accept", workspace.Owner))
        {
            acceptProposal.EnsureSuccessStatusCode();
        }

        using var supersede = await SendAsync(HttpMethod.Post, workspace, $"{path}/supersede", workspace.Owner, new { supersededById = proposal.Id });
        var superseded = await ReadAsync<DecisionDetails>(HttpMethod.Get, workspace, path);
        var accepted = await ReadAsync<List<DecisionSummary>>(HttpMethod.Get, workspace, $"{decisions}?status=Accepted");

        Assert.Equal(
            [
                HttpStatusCode.NotFound, HttpStatusCode.BadRequest, HttpStatusCode.Created, HttpStatusCode.OK, HttpStatusCode.Forbidden, HttpStatusCode.OK,
                HttpStatusCode.Conflict, HttpStatusCode.Conflict, HttpStatusCode.Conflict, HttpStatusCode.Conflict, HttpStatusCode.NotFound, HttpStatusCode.OK,
            ],
            new[]
            {
                withForeignTask.StatusCode, withoutOutcome.StatusCode, proposed.StatusCode, edit.StatusCode, acceptByContributor.StatusCode, accept.StatusCode,
                editAccepted.StatusCode, rejectAccepted.StatusCode, supersedeByProposal.StatusCode, supersedeBySelf.StatusCode, supersedeByForeign.StatusCode,
                supersede.StatusCode,
            });
        Assert.Equal((DecisionStatus.Superseded, proposal.Id, task.Id), (superseded.Status, superseded.SupersededById!.Value, superseded.TaskId!.Value));
        Assert.Equal(("Operar o banco", (string?)null, "Banco"), (superseded.Consequences, superseded.Alternatives, superseded.Title));
        Assert.Equal((workspace.Owner.Id, contributor.Id), (superseded.DecidedById!.Value, superseded.ProposedById));
        Assert.Equal([proposal.Id], accepted.Select(d => d.Id));
    }

    [Fact]
    public async Task CompositeForeignKeys_KeepVersionsAndReplacementsInsideTheirProject()
    {
        var workspace = await _kit.CreateWorkspaceAsync();
        var otherProject = await _kit.CreateSecondProjectAsync(workspace);
        var document = await _kit.CreateInProjectAsync<DocumentDetails>(workspace, "/documents", new { title = "Guia" });
        var decision = await _kit.CreateInProjectAsync<DecisionDetails>(workspace, "/decisions", new { title = "A", context = "B", outcome = "C" });
        var foreign = await CreateDecisionInAsync(workspace, otherProject.Id);

        var versionInOtherProject = await ExecuteAsync(
            """
            INSERT INTO project_document_versions (id, organization_id, project_id, document_id, number, title, body, author_id, created_at)
            VALUES (gen_random_uuid(), @organization, @project, @document, 9, 't', 'b', @author, now())
            """,
            ("organization", workspace.OrganizationId), ("project", otherProject.Id), ("document", document.Id), ("author", workspace.Owner.Id));
        var replacementInOtherProject = await ExecuteAsync(
            "UPDATE decisions SET superseded_by_id = @foreign WHERE id = @decision",
            ("foreign", foreign.Id), ("decision", decision.Id));

        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, versionInOtherProject);
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, replacementInOtherProject);
    }

    public void Dispose() => _factory.Dispose();

    private Task<HttpResponseMessage> SendAsync(HttpMethod method, Workspace workspace, string path, TestUser user, object? body = null) =>
        _kit.Projects.SendAsync(method, $"/api/v1{path}", user, body, workspace.OrganizationId.ToString());

    private async Task<T> ReadAsync<T>(HttpMethod method, Workspace workspace, string path)
    {
        using var response = await SendAsync(method, workspace, path, workspace.Owner);
        response.EnsureSuccessStatusCode();
        return await ProjectTestKit.ReadAsync<T>(response);
    }

    private async Task<DecisionDetails> CreateDecisionInAsync(Workspace workspace, Guid projectId)
    {
        using var response = await SendAsync(HttpMethod.Post, workspace, $"/projects/{projectId}/decisions", workspace.Owner, new { title = "Alheia", context = "X", outcome = "Y" });
        response.EnsureSuccessStatusCode();
        var decision = await ProjectTestKit.ReadAsync<DecisionDetails>(response);
        using var accept = await SendAsync(HttpMethod.Post, workspace, $"/decisions/{decision.Id}/accept", workspace.Owner);
        accept.EnsureSuccessStatusCode();
        return decision;
    }

    private async Task<string?> ExecuteAsync(string sql, params (string Name, Guid Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(_database.ConnectionString);
        await connection.OpenAsync(CancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        try
        {
            await command.ExecuteNonQueryAsync(CancellationToken);
            return null;
        }
        catch (PostgresException exception)
        {
            return exception.SqlState;
        }
    }
}
