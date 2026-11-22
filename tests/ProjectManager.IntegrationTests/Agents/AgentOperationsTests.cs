using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using ProjectManager.Application.Agents;
using ProjectManager.Application.Common;
using ProjectManager.Application.Workflows;
using ProjectManager.Domain.Activity;
using ProjectManager.Domain.Agents;
using ProjectManager.Infrastructure.Agents;
using ProjectManager.IntegrationTests.Agents.Probes;
using ProjectManager.IntegrationTests.Infrastructure;
using ProjectManager.IntegrationTests.Tasks;

namespace ProjectManager.IntegrationTests.Agents;

/// <summary>
/// Auditoria das operações de agentes e rate limiting (ADR-016, D10 e D11, etapa 10): uma linha por requisição com
/// credencial identificada, inclusive as recusadas, sem segredos; a linha sobrevive ao rollback da operação; o limite
/// por credencial é atômico no banco e grava o último uso uma vez por janela; tokens inexistentes contam por IP e são
/// barrados antes da consulta.
/// </summary>
public sealed class AgentOperationsTests(PostgresDatabaseFixture database, MailpitFixture mailpit) : IDisposable
{
    private static readonly string[] AllScopes = [.. AgentScopes.All];
    private static readonly string[] TasksReadOnly = [AgentScopes.TasksRead];

    private readonly List<IDisposable> _factories = [];

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task EveryRequest_IsRecorded_WithTheReasonOfTheRefusal_AndNoSecrets()
    {
        var (kit, workspace, agentId) = await SetUpAsync();
        var credential = await kit.Agents.CreateCredentialAsync(workspace.Owner, workspace.OrganizationId, agentId, AllScopes);
        var tasksOnly = await kit.Agents.CreateCredentialAsync(workspace.Owner, workspace.OrganizationId, agentId, TasksReadOnly);
        var token = credential.Token;
        var ungranted = (await kit.Tasks.CreateSecondProjectAsync(workspace, "Sem concessão")).Id;
        var viewer = (await kit.Tasks.CreateSecondProjectAsync(workspace, "Observado")).Id;
        await GrantAsync(kit, workspace, agentId, viewer, "Viewer");

        var task = await kit.Tasks.CreateTaskAsync(workspace, "Tarefa auditada");
        var workflow = await WorkflowAsync(kit, workspace);
        Guid State(string name) => workflow.States.Single(s => s.Name == name).Id;
        using (var shortcut = await kit.Tasks.Projects.SendAsync(
            HttpMethod.Post,
            "/api/v1/workflow/transitions",
            workspace.Owner,
            new { fromStateId = State("Backlog"), toStateId = State("Concluída"), requiresApproval = false },
            workspace.OrganizationId.ToString()))
        {
            Assert.Equal(HttpStatusCode.Created, shortcut.StatusCode);
        }

        var cli = new Dictionary<string, string> { ["X-Gestalyn-Client"] = "cli" };
        var replayKey = new Dictionary<string, string> { [IdempotencyRecord.HeaderName] = "chave-repetida" };

        await ExpectAsync(kit, token, HttpMethod.Get, "/api/v1/agents/projects", HttpStatusCode.OK, headers: cli);
        await ExpectAsync(kit, token, HttpMethod.Get, $"/api/v1/agents/projects/{ungranted}/context", HttpStatusCode.NotFound);
        await ExpectAsync(kit, tasksOnly.Token, HttpMethod.Get, $"/api/v1/agents/projects/{workspace.ProjectId}/context", HttpStatusCode.Forbidden);
        await ExpectAsync(kit, token, HttpMethod.Post, "/api/v1/agents/tasks", HttpStatusCode.Forbidden, new { projectId = viewer, title = "x" }, Key());
        await ExpectAsync(kit, token, HttpMethod.Get, "/api/v1/agents/projects", HttpStatusCode.NotFound,
            headers: new Dictionary<string, string> { ["X-Gestalyn-Organization"] = Guid.NewGuid().ToString() });
        await ExpectAsync(kit, token, HttpMethod.Post, $"/api/v1/agents/tasks/{task.Id}/transitions", HttpStatusCode.Forbidden,
            new { toStateId = State("Concluída"), expectedVersion = task.Version }, Key());
        await ExpectAsync(kit, token, HttpMethod.Post, "/api/v1/agents/tasks", HttpStatusCode.Created, new { projectId = workspace.ProjectId, title = "Repetida" }, replayKey);
        await ExpectAsync(kit, token, HttpMethod.Post, "/api/v1/agents/tasks", HttpStatusCode.Created, new { projectId = workspace.ProjectId, title = "Repetida" }, replayKey);

        var operations = (await OperationsAsync(kit, workspace, agentId)).Items.Reverse().ToList();
        Assert.Equal(
            [
                (AgentAuthorizationResult.Allowed, (string?)null, 200),
                (AgentAuthorizationResult.ProjectNotVisible, "project_not_visible", 404),
                (AgentAuthorizationResult.ScopeMissing, "scope_missing", 403),
                (AgentAuthorizationResult.PermissionMissing, "permission_missing", 403),
                (AgentAuthorizationResult.OrganizationMismatch, "organization_mismatch", 404),
                (AgentAuthorizationResult.BusinessRuleDenied, "agent_cannot_enter_done", 403),
                (AgentAuthorizationResult.Allowed, null, 201),
                (AgentAuthorizationResult.Allowed, null, 201),
            ],
            operations.Select(o => (o.AuthorizationResult, o.DenialCode, o.StatusCode)));

        // Modelo da rota, nunca a URL com valores; projeto e tarefa só quando o acesso foi concedido.
        Assert.Equal("/api/v1/agents/projects/{projectId:guid}/context", operations[1].RouteTemplate);
        Assert.Null(operations[1].ProjectId);
        Assert.Equal((workspace.ProjectId, task.Id), (operations[5].ProjectId, operations[5].TaskId));
        Assert.Equal((false, true), (operations[6].IdempotentReplay, operations[7].IdempotentReplay));
        Assert.Equal(ActivityOrigin.Cli, operations[0].ClientOrigin);
        Assert.Equal(tasksOnly.Credential.Id, operations[2].CredentialId);
        Assert.All(operations, o => Assert.False(string.IsNullOrEmpty(o.CorrelationId)));

        // Nenhuma coluna guarda o token, o prefixo ou o hash.
        var rows = await QueryAsync("SELECT row_to_json(o)::text FROM agent_operations o WHERE agent_id = @id", agentId);
        Assert.Equal(8, rows.Count);
        Assert.All(rows, row =>
        {
            Assert.DoesNotContain(token, row, StringComparison.Ordinal);
            Assert.DoesNotContain(credential.Credential.Prefix, row, StringComparison.Ordinal);
            Assert.DoesNotContain("Repetida", row, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task RejectedCredentials_AreRecorded_UnknownTokens_AreNot()
    {
        var clock = new ManualTimeProvider(DateTimeOffset.UtcNow);
        var (kit, workspace, agentId) = await SetUpAsync(timeProvider: clock);
        var revoked = await kit.Agents.CreateCredentialAsync(workspace.Owner, workspace.OrganizationId, agentId, AllScopes);
        var expiring = await kit.Agents.CreateCredentialAsync(workspace.Owner, workspace.OrganizationId, agentId, AllScopes, expiresInDays: 1);
        using (var revoke = await kit.Agents.ManageAsync(HttpMethod.Post, workspace.Owner, workspace.OrganizationId, $"/{agentId}/credentials/{revoked.Credential.Id}/revoke"))
        {
            revoke.EnsureSuccessStatusCode();
        }

        await ExpectAsync(kit, revoked.Token, HttpMethod.Get, "/api/v1/agents/me", HttpStatusCode.Unauthorized);
        await ExpectAsync(kit, "gsk_" + new string('A', 43), HttpMethod.Get, "/api/v1/agents/me", HttpStatusCode.Unauthorized);
        await ExpectAsync(kit, "gsk_curto", HttpMethod.Get, "/api/v1/agents/me", HttpStatusCode.Unauthorized);

        clock.UtcNow = expiring.Credential.ExpiresAt;
        await ExpectAsync(kit, expiring.Token, HttpMethod.Get, "/api/v1/agents/me", HttpStatusCode.Unauthorized);
        clock.UtcNow = DateTimeOffset.UtcNow;

        var active = await kit.Agents.CreateCredentialAsync(workspace.Owner, workspace.OrganizationId, agentId, AllScopes);
        using (var disable = await kit.Agents.ManageAsync(HttpMethod.Post, workspace.Owner, workspace.OrganizationId, $"/{agentId}/disable"))
        {
            disable.EnsureSuccessStatusCode();
        }

        await ExpectAsync(kit, active.Token, HttpMethod.Get, "/api/v1/agents/me", HttpStatusCode.Unauthorized);

        var operations = (await OperationsAsync(kit, workspace, agentId)).Items.Reverse().ToList();
        Assert.Equal(
            [
                (revoked.Credential.Id, AgentAuthorizationResult.CredentialRevoked),
                (expiring.Credential.Id, AgentAuthorizationResult.CredentialExpired),
                (active.Credential.Id, AgentAuthorizationResult.AgentDisabled),
            ],
            operations.Select(o => (o.CredentialId, o.AuthorizationResult)).OrderBy(o => o.AuthorizationResult));
        Assert.All(operations, o => Assert.Equal(401, o.StatusCode));
    }

    [Fact]
    public async Task TheOperation_SurvivesTheRollbackOfTheRequest()
    {
        var (kit, workspace, agentId) = await SetUpAsync(typeof(AgentIdempotencyProbeController));
        var token = (await kit.Agents.CreateCredentialAsync(workspace.Owner, workspace.OrganizationId, agentId, AllScopes)).Token;

        await ExpectAsync(kit, token, HttpMethod.Post, $"/api/v1/agents/__probe/idempotent/projects/{workspace.ProjectId}/throw",
            HttpStatusCode.InternalServerError, new { }, Key());

        var operation = Assert.Single((await OperationsAsync(kit, workspace, agentId)).Items);
        Assert.Equal((500, AgentAuthorizationResult.Allowed, workspace.ProjectId), (operation.StatusCode, operation.AuthorizationResult, operation.ProjectId));
        Assert.Equal(0L, (await QueryAsync("SELECT count(*)::text FROM tasks WHERE project_id = @id", workspace.ProjectId)).Select(long.Parse).Single());
    }

    [Fact]
    public async Task RateLimit_Returns429WithRetryAfter_AndRecordsOnlyTheFirstRejection()
    {
        var (kit, workspace, agentId) = await SetUpAsync(overrides: new() { ["RateLimiting:Agents:PermitLimit"] = "3", ["RateLimiting:Agents:WindowSeconds"] = "3600" });
        var credential = await kit.Agents.CreateCredentialAsync(workspace.Owner, workspace.OrganizationId, agentId, AllScopes);

        for (var i = 0; i < 3; i++)
        {
            await ExpectAsync(kit, credential.Token, HttpMethod.Get, "/api/v1/agents/me", HttpStatusCode.OK);
        }

        using (var limited = await kit.Agents.SendAsAgentAsync(credential.Token, HttpMethod.Get, "/api/v1/agents/me"))
        {
            Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
            Assert.InRange(limited.Headers.RetryAfter!.Delta!.Value.TotalSeconds, 1, 3600);
        }

        await ExpectAsync(kit, credential.Token, HttpMethod.Get, "/api/v1/agents/me", HttpStatusCode.TooManyRequests);

        // Outra credencial do mesmo agente tem a própria cota.
        var other = await kit.Agents.CreateCredentialAsync(workspace.Owner, workspace.OrganizationId, agentId, AllScopes);
        await ExpectAsync(kit, other.Token, HttpMethod.Get, "/api/v1/agents/me", HttpStatusCode.OK);

        var operations = (await OperationsAsync(kit, workspace, agentId)).Items.Where(o => o.CredentialId == credential.Credential.Id).ToList();
        Assert.Equal(3, operations.Count(o => o.AuthorizationResult == AgentAuthorizationResult.Allowed));
        var rejected = Assert.Single(operations, o => o.AuthorizationResult == AgentAuthorizationResult.RateLimited);
        Assert.Equal((429, "rate_limited"), (rejected.StatusCode, rejected.DenialCode));
    }

    [Fact]
    public async Task RateLimit_IsAtomicUnderConcurrency()
    {
        var (kit, workspace, agentId) = await SetUpAsync(overrides: new() { ["RateLimiting:Agents:PermitLimit"] = "5", ["RateLimiting:Agents:WindowSeconds"] = "3600" });
        var credential = await kit.Agents.CreateCredentialAsync(workspace.Owner, workspace.OrganizationId, agentId, AllScopes);

        var responses = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => kit.Agents.SendAsAgentAsync(credential.Token, HttpMethod.Get, "/api/v1/agents/me")));
        try
        {
            Assert.Equal(5, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
            Assert.Equal(7, responses.Count(r => r.StatusCode == HttpStatusCode.TooManyRequests));
        }
        finally
        {
            Array.ForEach(responses, r => r.Dispose());
        }

        var counts = await QueryAsync("SELECT request_count::text FROM agent_rate_limit_counters WHERE credential_id = @id", credential.Credential.Id);
        Assert.Equal(["12"], counts);
    }

    [Fact]
    public async Task LastUse_IsWrittenOnlyByTheFirstRequestOfTheWindow()
    {
        var (kit, workspace, agentId) = await SetUpAsync(overrides: new() { ["RateLimiting:Agents:WindowSeconds"] = "3600" });
        var credential = await kit.Agents.CreateCredentialAsync(workspace.Owner, workspace.OrganizationId, agentId, AllScopes);
        Assert.Null(credential.Credential.LastUsedAt);

        await ExpectAsync(kit, credential.Token, HttpMethod.Get, "/api/v1/agents/me", HttpStatusCode.OK);
        var first = await LastUsedAtAsync(kit, workspace, agentId, credential.Credential.Id);
        Assert.NotNull(first);

        // Um marcador antigo: a segunda requisição da janela não grava de novo.
        var marker = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        await ExecuteAsync("UPDATE agent_credentials SET last_used_at = @at WHERE id = @id", ("at", marker), ("id", credential.Credential.Id));
        await ExpectAsync(kit, credential.Token, HttpMethod.Get, "/api/v1/agents/me", HttpStatusCode.OK);
        Assert.Equal(marker, await LastUsedAtAsync(kit, workspace, agentId, credential.Credential.Id));

        // Na janela seguinte, grava de novo.
        await ExecuteAsync("DELETE FROM agent_rate_limit_counters WHERE credential_id = @id", ("id", credential.Credential.Id));
        await ExpectAsync(kit, credential.Token, HttpMethod.Get, "/api/v1/agents/me", HttpStatusCode.OK);
        Assert.True(await LastUsedAtAsync(kit, workspace, agentId, credential.Credential.Id) > marker);
    }

    [Fact]
    public async Task UnknownTokens_AreLimitedPerAddress_BeforeAnyLookup()
    {
        var (kit, workspace, agentId) = await SetUpAsync(overrides: new() { ["RateLimiting:AgentAuthFailures:PermitLimit"] = "3" });
        var valid = await kit.Agents.CreateCredentialAsync(workspace.Owner, workspace.OrganizationId, agentId, AllScopes);
        var factory = (ApiFactory)_factories[0];

        for (var i = 0; i < 3; i++)
        {
            await ExpectAsync(kit, "gsk_" + new string((char)('A' + i), 43), HttpMethod.Get, "/api/v1/agents/me", HttpStatusCode.Unauthorized);
        }

        using (var blocked = await kit.Agents.SendAsAgentAsync("gsk_" + new string('Z', 43), HttpMethod.Get, "/api/v1/agents/me"))
        {
            Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
            Assert.NotNull(blocked.Headers.RetryAfter);
        }

        // O bloqueio vem antes da consulta: nem uma credencial válida do mesmo IP é procurada, e nada vai para a auditoria.
        await ExpectAsync(kit, valid.Token, HttpMethod.Get, "/api/v1/agents/me", HttpStatusCode.TooManyRequests);
        Assert.Equal(3, factory.Logs.Entries.Count(entry => entry.Contains("Token de agente desconhecido", StringComparison.Ordinal)));
        Assert.Empty((await OperationsAsync(kit, workspace, agentId)).Items);

        // Rotas de pessoas não passam por esse limite.
        using var person = await kit.Tasks.SendToProjectAsync(HttpMethod.Get, workspace, "/tasks", workspace.Owner);
        Assert.Equal(HttpStatusCode.OK, person.StatusCode);
    }

    [Fact]
    public async Task AuditFailure_NeverBreaksTheRequest_AndDegradesTheHealthCheck()
    {
        var (kit, workspace, agentId) = await SetUpAsync();
        var factory = (ApiFactory)_factories[0];
        var writer = factory.Services.GetRequiredService<IAgentOperationWriter>();
        var health = factory.Services.GetRequiredService<AgentAuditHealth>();

        // Agente inexistente: a FK recusa a linha, e o gravador só registra o erro.
        await writer.WriteAsync(Entry(workspace.OrganizationId, Guid.NewGuid(), Guid.NewGuid()), CancellationToken);
        Assert.True(health.IsDegraded);
        Assert.Contains(factory.Logs.Entries, entry => entry.Contains("Falha ao gravar a operação", StringComparison.Ordinal));
        Assert.Equal("Degraded", await HealthStatusAsync(kit, "agents-audit"));

        // A próxima gravação bem-sucedida volta ao normal.
        var token = (await kit.Agents.CreateCredentialAsync(workspace.Owner, workspace.OrganizationId, agentId, AllScopes)).Token;
        await ExpectAsync(kit, token, HttpMethod.Get, "/api/v1/agents/me", HttpStatusCode.OK);
        Assert.False(health.IsDegraded);
        Assert.Equal("Healthy", await HealthStatusAsync(kit, "agents-audit"));
    }

    [Fact]
    public async Task OperationsRoute_FiltersAndIsolates()
    {
        var (kit, workspace, agentId) = await SetUpAsync();
        var token = (await kit.Agents.CreateCredentialAsync(workspace.Owner, workspace.OrganizationId, agentId, TasksReadOnly)).Token;
        await ExpectAsync(kit, token, HttpMethod.Get, "/api/v1/agents/me", HttpStatusCode.OK);
        await ExpectAsync(kit, token, HttpMethod.Get, "/api/v1/agents/projects", HttpStatusCode.Forbidden);

        var denied = await OperationsAsync(kit, workspace, agentId, "?result=ScopeMissing");
        Assert.Equal([AgentAuthorizationResult.ScopeMissing], denied.Items.Select(o => o.AuthorizationResult));
        Assert.Empty((await OperationsAsync(kit, workspace, agentId, $"?from={Uri.EscapeDataString(DateTimeOffset.UtcNow.AddHours(1).ToString("O"))}")).Items);
        Assert.Equal(2, (await OperationsAsync(kit, workspace, agentId, "?pageSize=1")).TotalCount);

        using (var inverted = await kit.Agents.ManageAsync(HttpMethod.Get, workspace.Owner, workspace.OrganizationId,
            $"/{agentId}/operations?from={Uri.EscapeDataString(DateTimeOffset.UtcNow.ToString("O"))}&to={Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(-1).ToString("O"))}"))
        {
            Assert.Equal(HttpStatusCode.BadRequest, inverted.StatusCode);
        }

        // Agente de outra organização: 404; pessoa sem agents.manage: 403.
        var (foreignOwner, foreignOrganization) = await kit.Agents.CreateOwnerAsync();
        using (var foreign = await kit.Agents.ManageAsync(HttpMethod.Get, foreignOwner, foreignOrganization, $"/{agentId}/operations"))
        {
            Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        }

        var member = await kit.Tasks.AddTeamMemberAsync(workspace, "Contributor");
        using var forbidden = await kit.Agents.ManageAsync(HttpMethod.Get, member, workspace.OrganizationId, $"/{agentId}/operations");
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }

    public void Dispose() => _factories.ForEach(factory => factory.Dispose());

    private static Dictionary<string, string> Key() => new() { [IdempotencyRecord.HeaderName] = Guid.NewGuid().ToString() };

    private static AgentOperationEntry Entry(Guid organizationId, Guid agentId, Guid credentialId) =>
        new(organizationId, agentId, credentialId, DateTimeOffset.UtcNow, 1, "GET", "/api/v1/agents/me", null, null, 200,
            AgentAuthorizationResult.Allowed, null, "teste", false, ActivityOrigin.Api);

    private async Task<(Kit Kit, Workspace Workspace, Guid AgentId)> SetUpAsync(
        Type? probe = null,
        Dictionary<string, string?>? overrides = null,
        TimeProvider? timeProvider = null)
    {
        var factory = new ApiFactory(database.ConnectionString, mailpit, overrides, timeProvider);
        _factories.Add(factory);
        var client = probe is null ? factory.CreateClient() : factory.WithProbes(probe).CreateClient();
        var kit = new Kit(new TaskTestKit(client, mailpit), new AgentTestKit(client, mailpit));

        var workspace = await kit.Tasks.CreateWorkspaceAsync("Projeto auditado");
        var agentId = (await kit.Agents.CreateAgentAsync(workspace.Owner, workspace.OrganizationId, "Agente auditado")).Id;
        await GrantAsync(kit, workspace, agentId, workspace.ProjectId, "Contributor");
        return (kit, workspace, agentId);
    }

    private static async Task GrantAsync(Kit kit, Workspace workspace, Guid agentId, Guid projectId, string role)
    {
        using var response = await kit.Agents.ManageAsync(HttpMethod.Put, workspace.Owner, workspace.OrganizationId, $"/{agentId}/projects/{projectId}", new { role });
        response.EnsureSuccessStatusCode();
    }

    private static async Task ExpectAsync(
        Kit kit,
        string token,
        HttpMethod method,
        string path,
        HttpStatusCode expected,
        object? body = null,
        IReadOnlyDictionary<string, string>? headers = null)
    {
        using var response = await kit.Agents.SendAsAgentAsync(token, method, path, body, headers);
        Assert.True(response.StatusCode == expected, $"{method} {path}: {response.StatusCode}, esperado {expected}");
    }

    private static async Task<PagedResult<AgentOperationItem>> OperationsAsync(Kit kit, Workspace workspace, Guid agentId, string query = "?pageSize=100")
    {
        using var response = await kit.Agents.ManageAsync(HttpMethod.Get, workspace.Owner, workspace.OrganizationId, $"/{agentId}/operations{query}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await TaskTestKit.ReadAsync<PagedResult<AgentOperationItem>>(response);
    }

    private static async Task<DateTimeOffset?> LastUsedAtAsync(Kit kit, Workspace workspace, Guid agentId, Guid credentialId)
    {
        using var response = await kit.Agents.ManageAsync(HttpMethod.Get, workspace.Owner, workspace.OrganizationId, $"/{agentId}/credentials");
        return (await TaskTestKit.ReadAsync<List<AgentCredentialDetails>>(response)).Single(c => c.Id == credentialId).LastUsedAt;
    }

    private static async Task<WorkflowDetails> WorkflowAsync(Kit kit, Workspace workspace)
    {
        using var response = await kit.Tasks.SendToProjectAsync(HttpMethod.Get, workspace, "/workflow", workspace.Owner);
        return await TaskTestKit.ReadAsync<WorkflowDetails>(response);
    }

    private static async Task<string> HealthStatusAsync(Kit kit, string check)
    {
        using var response = await kit.Agents.SendAsAgentAsync("sem-token", HttpMethod.Get, "/health");
        using var document = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        return document.RootElement.GetProperty("checks").EnumerateArray()
            .Single(c => c.GetProperty("name").GetString() == check)
            .GetProperty("status").GetString()!;
    }

    private async Task<List<string>> QueryAsync(string sql, Guid id)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync(CancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync(CancellationToken);
        var rows = new List<string>();
        while (await reader.ReadAsync(CancellationToken))
        {
            rows.Add(reader.GetString(0));
        }

        return rows;
    }

    private async Task ExecuteAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync(CancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await command.ExecuteNonQueryAsync(CancellationToken);
    }

    private sealed record Kit(TaskTestKit Tasks, AgentTestKit Agents);
}
