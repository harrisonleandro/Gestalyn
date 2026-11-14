using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using ProjectManager.Application.Common;
using ProjectManager.Application.Planning;
using ProjectManager.Application.Projects;
using ProjectManager.Application.Tasks;
using ProjectManager.IntegrationTests.Infrastructure;

namespace ProjectManager.IntegrationTests.Tasks;

/// <summary>
/// Entradas inválidas respondem 400, nunca 500 (auditoria de 2026-11-12): nome em branco no POST e no
/// PATCH, e páginas fora do intervalo nas listagens.
/// </summary>
public sealed class InputValidationTests : IDisposable
{
    public static TheoryData<string> BlankNames => ["", " ", "   ", "\t\n", " "];

    private readonly ApiFactory _factory;
    private readonly HttpClient _client;
    private readonly TaskTestKit _kit;

    public InputValidationTests(PostgresDatabaseFixture database, MailpitFixture mailpit)
    {
        _factory = new ApiFactory(database.ConnectionString, mailpit);
        _client = _factory.CreateClient();
        _kit = new TaskTestKit(_client, mailpit);
    }

    [Theory]
    [MemberData(nameof(BlankNames))]
    public async Task BlankNames_AreRejectedWith400_OnCreateAndPatch_AndNothingChanges(string blank)
    {
        var workspace = await _kit.CreateWorkspaceAsync("Projeto original");
        var phase = await _kit.CreateInProjectAsync<PhaseDetails>(workspace, "/phases", new { name = "Fase original" });
        var milestone = await _kit.CreateInProjectAsync<MilestoneDetails>(workspace, "/milestones", new { name = "Marco original" });
        var task = await _kit.CreateTaskAsync(workspace, "Tarefa original");

        (string Label, Func<Task<HttpResponseMessage>> Send, string Field)[] requests =
        [
            ("POST projeto", () => _kit.Projects.CreateAsync(workspace.Owner, workspace.OrganizationId, new { name = blank }), "Name"),
            ("PATCH projeto", () => _kit.SendToProjectAsync(HttpMethod.Patch, workspace, "", workspace.Owner, new { name = blank }), "Name"),
            ("POST fase", () => _kit.SendToProjectAsync(HttpMethod.Post, workspace, "/phases", workspace.Owner, new { name = blank }), "Name"),
            ("PATCH fase", () => _kit.SendToProjectAsync(HttpMethod.Patch, workspace, $"/phases/{phase.Id}", workspace.Owner, new { name = blank }), "Name"),
            ("POST marco", () => _kit.SendToProjectAsync(HttpMethod.Post, workspace, "/milestones", workspace.Owner, new { name = blank }), "Name"),
            ("PATCH marco", () => _kit.SendToProjectAsync(HttpMethod.Patch, workspace, $"/milestones/{milestone.Id}", workspace.Owner, new { name = blank }), "Name"),
            ("POST tarefa", () => _kit.SendToProjectAsync(HttpMethod.Post, workspace, "/tasks", workspace.Owner, new { title = blank }), "Title"),
            ("PATCH tarefa", () => _kit.SendToTaskAsync(HttpMethod.Patch, workspace, task.Id, "", workspace.Owner, new { title = blank }), "Title"),
        ];

        foreach (var (label, send, field) in requests)
        {
            using var response = await send();
            Assert.True(response.StatusCode == HttpStatusCode.BadRequest, $"{label}: esperado 400, obtido {response.StatusCode}.");

            var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(TestContext.Current.CancellationToken);
            Assert.True(problem!.Errors.ContainsKey(field), $"{label}: erro esperado no campo {field}.");
        }

        using var project = await _kit.SendToProjectAsync(HttpMethod.Get, workspace, "", workspace.Owner);
        using var phases = await _kit.SendToProjectAsync(HttpMethod.Get, workspace, "/phases", workspace.Owner);
        using var milestones = await _kit.SendToProjectAsync(HttpMethod.Get, workspace, "/milestones", workspace.Owner);
        using var taskDetails = await _kit.SendToTaskAsync(HttpMethod.Get, workspace, task.Id, "", workspace.Owner);

        Assert.Equal("Projeto original", (await TaskTestKit.ReadAsync<ProjectDetails>(project)).Name);
        Assert.Equal(["Fase original"], (await TaskTestKit.ReadAsync<List<PhaseDetails>>(phases)).Select(p => p.Name));
        Assert.Equal(["Marco original"], (await TaskTestKit.ReadAsync<List<MilestoneDetails>>(milestones)).Select(m => m.Name));
        Assert.Equal("Tarefa original", (await TaskTestKit.ReadAsync<TaskDetails>(taskDetails)).Title);
    }

    [Theory]
    [InlineData("page=1", HttpStatusCode.OK, 1)]
    [InlineData("page=2", HttpStatusCode.OK, 0)]
    [InlineData("page=1000", HttpStatusCode.OK, 0)]
    [InlineData("page=21474837&pageSize=100", HttpStatusCode.OK, 0)]
    [InlineData("page=21474838&pageSize=100", HttpStatusCode.BadRequest, -1)]
    [InlineData("page=42949674", HttpStatusCode.BadRequest, -1)]
    [InlineData("page=2147483647", HttpStatusCode.BadRequest, -1)]
    [InlineData("page=0", HttpStatusCode.BadRequest, -1)]
    [InlineData("page=-1", HttpStatusCode.BadRequest, -1)]
    [InlineData("pageSize=0", HttpStatusCode.BadRequest, -1)]
    [InlineData("pageSize=101", HttpStatusCode.BadRequest, -1)]
    public async Task Pagination_NeverFailsWithAServerError(string query, HttpStatusCode expected, int expectedItems)
    {
        var workspace = await _kit.CreateWorkspaceAsync();
        await _kit.CreateTaskAsync(workspace, "Única tarefa");

        using var projects = await _kit.Projects.ListAsync(workspace.Owner, workspace.OrganizationId, $"?{query}");
        using var tasks = await _kit.SendToProjectAsync(HttpMethod.Get, workspace, $"/tasks?{query}", workspace.Owner);

        Assert.Equal(expected, projects.StatusCode);
        Assert.Equal(expected, tasks.StatusCode);
        if (expected == HttpStatusCode.OK)
        {
            var projectPage = await TaskTestKit.ReadAsync<PagedResult<ProjectSummary>>(projects);
            var taskPage = await TaskTestKit.ReadAsync<PagedResult<TaskSummary>>(tasks);
            Assert.Equal((expectedItems, 1), (projectPage.Items.Count, projectPage.TotalCount));
            Assert.Equal((expectedItems, 1), (taskPage.Items.Count, taskPage.TotalCount));
        }
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }
}
