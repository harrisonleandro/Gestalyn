using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using ProjectManager.Application.Agents;
using ProjectManager.Application.Common;
using ProjectManager.Application.Tasks;
using ProjectManager.Domain.Agents;
using ProjectManager.IntegrationTests.Agents.Probes;
using ProjectManager.IntegrationTests.Infrastructure;
using ProjectManager.IntegrationTests.Tasks;

namespace ProjectManager.IntegrationTests.Agents;

/// <summary>
/// Idempotência obrigatória dos POSTs de agentes (ADR-016, D9, etapa 8): a mesma chave repete a resposta sem repetir
/// a operação; outra requisição com a mesma chave é 422; a resposta só é devolvida depois de reavaliar o acesso; a
/// operação e a resposta são gravadas juntas, e uma falha não deixa nem uma nem outra.
/// </summary>
public sealed class AgentIdempotencyTests : IAsyncLifetime
{
    private static readonly string[] AllScopes = [.. AgentScopes.All];

    private readonly ApiFactory _factory;
    private readonly WebApplicationFactory<Program> _withProbe;
    private readonly TaskTestKit _tasks;
    private readonly AgentTestKit _agents;

    private Workspace _workspace = null!;
    private Guid _agentId;
    private CreatedAgentCredential _credential = null!;

    public AgentIdempotencyTests(PostgresDatabaseFixture database, MailpitFixture mailpit)
    {
        _factory = new ApiFactory(database.ConnectionString, mailpit);
        _withProbe = _factory.WithProbes(typeof(AgentIdempotencyProbeController));
        var client = _withProbe.CreateClient();
        _tasks = new TaskTestKit(client, mailpit);
        _agents = new AgentTestKit(client, mailpit);
    }

    private string Token => _credential.Token;

    public async ValueTask InitializeAsync()
    {
        _workspace = await _tasks.CreateWorkspaceAsync("Projeto idempotente");
        _agentId = (await _agents.CreateAgentAsync(_workspace.Owner, _workspace.OrganizationId, "Agente idempotente")).Id;
        await GrantAsync(_workspace.ProjectId, "Contributor");
        _credential = await CredentialAsync();
    }

    [Fact]
    public async Task SameRequest_ReplaysTheOriginalResponse_WithoutRepeatingTheOperation()
    {
        var key = NewKey();
        var body = new { projectId = _workspace.ProjectId, title = "Uma vez só" };

        using var first = await CreateTaskAsync(Token, key, body);
        using var second = await CreateTaskAsync(Token, key, body);

        Assert.Equal((HttpStatusCode.Created, HttpStatusCode.Created), (first.StatusCode, second.StatusCode));
        Assert.False(first.Headers.Contains(IdempotencyRecord.ReplayedHeaderName));
        Assert.Equal(["true"], second.Headers.GetValues(IdempotencyRecord.ReplayedHeaderName));
        Assert.Equal(first.Headers.Location, second.Headers.Location);
        Assert.Equal(
            (await TaskTestKit.ReadAsync<TaskDetails>(first)).Id,
            (await TaskTestKit.ReadAsync<TaskDetails>(second)).Id);
        Assert.Equal(["Uma vez só"], await TaskTitlesAsync());
    }

    [Fact]
    public async Task SameKey_WithAnotherBodyRouteOrTask_IsUnprocessable()
    {
        var key = NewKey();
        using (var created = await CreateTaskAsync(Token, key, new { projectId = _workspace.ProjectId, title = "Original" }))
        {
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        }

        using (var otherBody = await CreateTaskAsync(Token, key, new { projectId = _workspace.ProjectId, title = "Outro corpo" }))
        {
            Assert.Equal(HttpStatusCode.UnprocessableEntity, otherBody.StatusCode);
        }

        using (var otherRoute = await PostAsync(Token, key, "/api/v1/agents/activities", new { projectId = _workspace.ProjectId, kind = "Note", summary = "x" }))
        {
            Assert.Equal(HttpStatusCode.UnprocessableEntity, otherRoute.StatusCode);
        }

        // Mesmo corpo em outra tarefa: o ID do caminho entra no hash, e a resposta da primeira nunca é devolvida.
        var first = await NewTaskAsync("Primeira");
        var second = await NewTaskAsync("Segunda");
        var toDo = await StateIdAsync(first.Id, "A fazer");
        var transitionKey = NewKey();
        var transition = new { toStateId = toDo, expectedVersion = 1 };
        using (var moved = await PostAsync(Token, transitionKey, $"/api/v1/agents/tasks/{first.Id}/transitions", transition))
        {
            Assert.Equal(HttpStatusCode.OK, moved.StatusCode);
        }

        using (var otherTask = await PostAsync(Token, transitionKey, $"/api/v1/agents/tasks/{second.Id}/transitions", transition))
        {
            Assert.Equal(HttpStatusCode.UnprocessableEntity, otherTask.StatusCode);
        }

        Assert.Equal(1, (await GetTaskAsync(second.Id)).Version);
        Assert.Equal(["Original", "Primeira", "Segunda"], (await TaskTitlesAsync()).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task MissingOrInvalidKey_IsBadRequest_AndNothingIsCreated()
    {
        var body = new { projectId = _workspace.ProjectId, title = "Sem chave válida" };

        using (var missing = await _agents.SendAsAgentAsync(Token, HttpMethod.Post, "/api/v1/agents/tasks", body))
        {
            Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        }

        foreach (var invalid in new[] { "com espaço", new string('k', IdempotencyRecord.KeyMaxLength + 1) })
        {
            using var response = await CreateTaskAsync(Token, invalid, body);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        using (var longest = await CreateTaskAsync(Token, new string('k', IdempotencyRecord.KeyMaxLength), body))
        {
            Assert.Equal(HttpStatusCode.Created, longest.StatusCode);
        }

        Assert.Equal(["Sem chave válida"], await TaskTitlesAsync());
    }

    [Fact]
    public async Task Key_IsPerCredential()
    {
        var other = await CredentialAsync();
        var key = NewKey();
        var body = new { projectId = _workspace.ProjectId, title = "Por credencial" };

        using var first = await CreateTaskAsync(Token, key, body);
        using var second = await CreateTaskAsync(other.Token, key, body);

        Assert.Equal((HttpStatusCode.Created, HttpStatusCode.Created), (first.StatusCode, second.StatusCode));
        Assert.False(second.Headers.Contains(IdempotencyRecord.ReplayedHeaderName));
        Assert.Equal(2, (await TaskTitlesAsync()).Count);
    }

    [Fact]
    public async Task ConcurrentRequests_WithTheSameKey_CreateExactlyOneTask()
    {
        var key = NewKey();
        var body = new { projectId = _workspace.ProjectId, title = "Concorrente" };

        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => CreateTaskAsync(Token, key, body)));
        try
        {
            Assert.All(responses, r => Assert.Contains(r.StatusCode, new[] { HttpStatusCode.Created, HttpStatusCode.Conflict }));
            var created = responses.Where(r => r.StatusCode == HttpStatusCode.Created).ToList();
            Assert.NotEmpty(created);
            var ids = new HashSet<Guid>();
            foreach (var response in created)
            {
                ids.Add((await TaskTestKit.ReadAsync<TaskDetails>(response)).Id);
            }

            Assert.Single(ids);
            Assert.Equal(["Concorrente"], await TaskTitlesAsync());
        }
        finally
        {
            Array.ForEach(responses, r => r.Dispose());
        }
    }

    [Fact]
    public async Task Replay_ReauthorizesTheCredential_BeforeReturningTheStoredResponse()
    {
        var key = NewKey();
        var body = new { projectId = _workspace.ProjectId, title = "Reautorizada" };
        Assert.Equal(HttpStatusCode.Created, await CreateTaskStatusAsync(Token, key, body));

        await GrantAsync(_workspace.ProjectId, "Viewer");
        Assert.Equal(HttpStatusCode.Forbidden, await CreateTaskStatusAsync(Token, key, body));

        using (var revoke = await _agents.ManageAsync(HttpMethod.Delete, _workspace.Owner, _workspace.OrganizationId, $"/{_agentId}/projects/{_workspace.ProjectId}"))
        {
            revoke.EnsureSuccessStatusCode();
        }

        Assert.Equal(HttpStatusCode.NotFound, await CreateTaskStatusAsync(Token, key, body));

        await GrantAsync(_workspace.ProjectId, "Contributor");
        using (var replay = await CreateTaskAsync(Token, key, body))
        {
            Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
            Assert.True(replay.Headers.Contains(IdempotencyRecord.ReplayedHeaderName));
        }

        using (var revoke = await _agents.ManageAsync(
            HttpMethod.Post, _workspace.Owner, _workspace.OrganizationId, $"/{_agentId}/credentials/{_credential.Credential.Id}/revoke"))
        {
            revoke.EnsureSuccessStatusCode();
        }

        Assert.Equal(HttpStatusCode.Unauthorized, await CreateTaskStatusAsync(Token, key, body));
        Assert.Equal(["Reautorizada"], await TaskTitlesAsync());
    }

    [Fact]
    public async Task RefusalsThatDependOnAccess_AreNotStored_ButDeterministicOnesAre()
    {
        var other = (await _tasks.CreateSecondProjectAsync(_workspace, "Ainda sem concessão")).Id;
        var key = NewKey();
        var body = new { projectId = other, title = "Depois da concessão" };

        Assert.Equal(HttpStatusCode.NotFound, await CreateTaskStatusAsync(Token, key, body));
        await GrantAsync(other, "Contributor");
        using (var afterGrant = await CreateTaskAsync(Token, key, body))
        {
            Assert.Equal(HttpStatusCode.Created, afterGrant.StatusCode);
            Assert.False(afterGrant.Headers.Contains(IdempotencyRecord.ReplayedHeaderName));
        }

        // Validação (400) e versão desatualizada (409) são guardadas e repetidas.
        var invalidKey = NewKey();
        var invalid = new { projectId = _workspace.ProjectId, title = new string('t', 1000) };
        Assert.Equal(HttpStatusCode.BadRequest, await CreateTaskStatusAsync(Token, invalidKey, invalid));
        using (var replayed = await CreateTaskAsync(Token, invalidKey, invalid))
        {
            Assert.Equal(HttpStatusCode.BadRequest, replayed.StatusCode);
            Assert.True(replayed.Headers.Contains(IdempotencyRecord.ReplayedHeaderName));
        }

        var task = await NewTaskAsync("Versão antiga");
        var staleKey = NewKey();
        var stale = new { toStateId = await StateIdAsync(task.Id, "A fazer"), expectedVersion = 7 };
        var path = $"/api/v1/agents/tasks/{task.Id}/transitions";
        using (var conflict = await PostAsync(Token, staleKey, path, stale))
        {
            Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        }

        using (var replayedConflict = await PostAsync(Token, staleKey, path, stale))
        {
            Assert.Equal(HttpStatusCode.Conflict, replayedConflict.StatusCode);
            Assert.True(replayedConflict.Headers.Contains(IdempotencyRecord.ReplayedHeaderName));
        }
    }

    [Fact]
    public async Task OperationAndResponse_AreRecordedTogether_OrNotAtAll()
    {
        // Exceção depois da gravação: nada fica, e a mesma chave executa de novo (não ficou guardada nem presa).
        var throwKey = NewKey();
        Assert.Equal(HttpStatusCode.InternalServerError, await ProbeAsync(throwKey, "throw"));
        Assert.Equal(HttpStatusCode.InternalServerError, await ProbeAsync(throwKey, "throw"));

        // 503 não é guardado e desfaz a operação.
        Assert.Equal(HttpStatusCode.ServiceUnavailable, await ProbeAsync(NewKey(), "unavailable"));

        // 409 é guardado, mas o que a operação gravou é desfeito: a repetição não cria nada.
        var conflictKey = NewKey();
        Assert.Equal(HttpStatusCode.Conflict, await ProbeAsync(conflictKey, "conflict"));
        Assert.Equal(HttpStatusCode.Conflict, await ProbeAsync(conflictKey, "conflict"));
        Assert.Empty(await TaskTitlesAsync());

        var okKey = NewKey();
        Assert.Equal(HttpStatusCode.OK, await ProbeAsync(okKey, "ok"));
        Assert.Equal(HttpStatusCode.OK, await ProbeAsync(okKey, "ok"));
        Assert.Equal(["Sonda ok"], await TaskTitlesAsync());
    }

    public async ValueTask DisposeAsync()
    {
        await _withProbe.DisposeAsync();
        await _factory.DisposeAsync();
    }

    private static string NewKey() => Guid.NewGuid().ToString("N");

    private Task<HttpResponseMessage> PostAsync(string token, string key, string path, object body) =>
        _agents.SendAsAgentAsync(token, HttpMethod.Post, path, body, new Dictionary<string, string> { [IdempotencyRecord.HeaderName] = key });

    private Task<HttpResponseMessage> CreateTaskAsync(string token, string key, object body) =>
        PostAsync(token, key, "/api/v1/agents/tasks", body);

    private async Task<HttpStatusCode> CreateTaskStatusAsync(string token, string key, object body)
    {
        using var response = await CreateTaskAsync(token, key, body);
        return response.StatusCode;
    }

    private async Task<HttpStatusCode> ProbeAsync(string key, string outcome)
    {
        using var response = await PostAsync(Token, key, $"/api/v1/agents/__probe/idempotent/projects/{_workspace.ProjectId}/{outcome}", new { });
        return response.StatusCode;
    }

    private async Task<TaskDetails> NewTaskAsync(string title)
    {
        using var response = await CreateTaskAsync(Token, NewKey(), new { projectId = _workspace.ProjectId, title });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await TaskTestKit.ReadAsync<TaskDetails>(response);
    }

    private async Task<TaskDetails> GetTaskAsync(Guid taskId)
    {
        using var response = await _agents.SendAsAgentAsync(Token, HttpMethod.Get, $"/api/v1/agents/tasks/{taskId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await TaskTestKit.ReadAsync<TaskDetails>(response);
    }

    private async Task<Guid> StateIdAsync(Guid taskId, string name) =>
        (await GetTaskAsync(taskId)).AvailableTransitions.Single(t => t.ToStateName == name).ToStateId;

    /// <summary>Títulos das tarefas do projeto, lidos pela pessoa (não depende do acesso do agente).</summary>
    private async Task<IReadOnlyList<string>> TaskTitlesAsync()
    {
        using var response = await _tasks.SendToProjectAsync(HttpMethod.Get, _workspace, "/tasks?pageSize=100", _workspace.Owner);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return [.. (await TaskTestKit.ReadAsync<PagedResult<TaskSummary>>(response)).Items.Select(t => t.Title)];
    }

    private Task<CreatedAgentCredential> CredentialAsync() =>
        _agents.CreateCredentialAsync(_workspace.Owner, _workspace.OrganizationId, _agentId, AllScopes);

    private async Task GrantAsync(Guid projectId, string role)
    {
        using var response = await _agents.ManageAsync(HttpMethod.Put, _workspace.Owner, _workspace.OrganizationId, $"/{_agentId}/projects/{projectId}", new { role });
        response.EnsureSuccessStatusCode();
    }
}
