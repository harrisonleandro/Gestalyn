using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using ProjectManager.Application.Agents;
using ProjectManager.Domain.Agents;
using ProjectManager.Domain.Projects;
using ProjectManager.IntegrationTests.Infrastructure;
using ProjectManager.IntegrationTests.Organizations;
using ProjectManager.IntegrationTests.Projects;

namespace ProjectManager.IntegrationTests.Agents;

/// <summary>
/// Gestão de agentes por pessoas (ADR-016): ciclo de vida, credencial com o token mostrado uma única vez, limite de
/// credenciais ativas, acesso a projetos com as duas permissões, permissões por papel e isolamento entre organizações.
/// </summary>
public sealed class AgentManagementTests : IDisposable
{
    private readonly PostgresDatabaseFixture _database;
    private readonly ApiFactory _factory;
    private readonly ProjectTestKit _kit;

    public AgentManagementTests(PostgresDatabaseFixture database, MailpitFixture mailpit)
    {
        _database = database;
        _factory = new ApiFactory(database.ConnectionString, mailpit);
        _kit = new ProjectTestKit(_factory.CreateClient(), mailpit);
    }

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    private OrganizationTestKit Organizations => _kit.Organizations;

    [Fact]
    public async Task Owner_ManagesTheAgentLifecycle()
    {
        var (owner, organizationId) = await CreateOwnerAsync();
        var agentsPath = $"/api/v1/organizations/{organizationId}/agents";

        var agent = await CreateAgentAsync(owner, organizationId, "Revisor", "Revisa PRs");
        using (var duplicate = await _kit.SendAsync(HttpMethod.Post, agentsPath, owner, new { name = " REVISOR " }))
        {
            Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        }

        using (var update = await _kit.SendAsync(HttpMethod.Patch, $"{agentsPath}/{agent.Id}", owner, new { name = "Revisor de código", description = (string?)null }))
        {
            var updated = await ProjectTestKit.ReadAsync<AgentDetails>(update);
            Assert.Equal(("Revisor de código", (string?)null), (updated.Name, updated.Description));
        }

        using (var disable = await _kit.SendAsync(HttpMethod.Post, $"{agentsPath}/{agent.Id}/disable", owner))
        {
            Assert.Equal(AgentStatus.Disabled, (await ProjectTestKit.ReadAsync<AgentDetails>(disable)).Status);
        }

        using (var whileDisabled = await CreateCredentialAsync(owner, organizationId, agent.Id, new { name = "CI", scopes = new[] { AgentScopes.TasksRead } }))
        {
            Assert.Equal(HttpStatusCode.Conflict, whileDisabled.StatusCode);
        }

        using (var enable = await _kit.SendAsync(HttpMethod.Post, $"{agentsPath}/{agent.Id}/enable", owner))
        {
            Assert.Equal(AgentStatus.Active, (await ProjectTestKit.ReadAsync<AgentDetails>(enable)).Status);
        }

        using var list = await _kit.SendAsync(HttpMethod.Get, agentsPath, owner);
        var summary = Assert.Single(await ProjectTestKit.ReadAsync<List<AgentSummary>>(list));
        Assert.Equal((agent.Id, "Revisor de código", AgentStatus.Active), (summary.Id, summary.Name, summary.Status));
    }

    [Fact]
    public async Task Credential_TokenIsShownOnlyOnCreation_AndOnlyTheHashAndPrefixAreStored()
    {
        var (owner, organizationId) = await CreateOwnerAsync();
        var agent = await CreateAgentAsync(owner, organizationId, "Agente de CI");
        var before = DateTimeOffset.UtcNow;

        using var creation = await CreateCredentialAsync(
            owner,
            organizationId,
            agent.Id,
            new { name = "Pipeline", scopes = new[] { AgentScopes.TasksWrite, AgentScopes.ContextRead } });
        Assert.Equal(HttpStatusCode.Created, creation.StatusCode);
        Assert.True(creation.Headers.CacheControl?.NoStore);
        var created = await ProjectTestKit.ReadAsync<CreatedAgentCredential>(creation);

        Assert.True(AgentTokens.IsWellFormed(created.Token));
        Assert.Equal(created.Token.Substring(4, 8), created.Credential.Prefix);
        Assert.Equal([AgentScopes.ContextRead, AgentScopes.TasksWrite], created.Credential.Scopes);
        Assert.Equal(AgentCredentialState.Active, created.Credential.State);
        Assert.InRange(created.Credential.ExpiresAt, before.AddDays(90).AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(90).AddMinutes(1));

        // Nenhuma leitura posterior devolve o token.
        foreach (var path in new[] { $"/api/v1/organizations/{organizationId}/agents/{agent.Id}", $"/api/v1/organizations/{organizationId}/agents/{agent.Id}/credentials" })
        {
            using var read = await _kit.SendAsync(HttpMethod.Get, path, owner);
            var body = await read.Content.ReadAsStringAsync(CancellationToken);
            Assert.Contains(created.Credential.Prefix, body, StringComparison.Ordinal);
            Assert.DoesNotContain(created.Token, body, StringComparison.Ordinal);
            Assert.DoesNotContain(created.Token[AgentTokens.Marker.Length..], body, StringComparison.Ordinal);
        }

        // No banco: o SHA-256 do token completo; o token não aparece em credenciais nem em eventos.
        var expectedHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(created.Token)));
        await using var context = _database.CreateContext(organizationId);
        var stored = await context.AgentCredentials.SingleAsync(c => c.Id == created.Credential.Id, CancellationToken);
        Assert.Equal(expectedHash, stored.TokenHash);
        Assert.Equal(0, await CountRowsContainingAsync(created.Token[AgentTokens.Marker.Length..]));
        Assert.Equal(0, await CountRowsContainingAsync(expectedHash, includeCredentials: false));
        Assert.DoesNotContain(_factory.Logs.Entries, entry => entry.Contains(created.Token[AgentTokens.Marker.Length..], StringComparison.Ordinal));
    }

    [Fact]
    public async Task Credential_ValidatesScopesAndLifetime_AndAllowsAtMostThreeActive()
    {
        var (owner, organizationId) = await CreateOwnerAsync();
        var agent = await CreateAgentAsync(owner, organizationId, "Agente");
        string[] read = [AgentScopes.TasksRead];

        object[] invalid =
        [
            new { name = "Sem escopo", scopes = Array.Empty<string>() },
            new { name = "Aprovação", scopes = new[] { "tasks:approve" } },
            new { name = "Longa", scopes = read, expiresInDays = 366 },
            new { name = "Curta", scopes = read, expiresInDays = 0 },
        ];
        foreach (var body in invalid)
        {
            using var response = await CreateCredentialAsync(owner, organizationId, agent.Id, body);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        var credentials = new List<CreatedAgentCredential>();
        for (var i = 1; i <= AgentCredential.MaxActivePerAgent; i++)
        {
            using var response = await CreateCredentialAsync(owner, organizationId, agent.Id, new { name = $"C{i}", scopes = read, expiresInDays = 30 });
            credentials.Add(await ProjectTestKit.ReadAsync<CreatedAgentCredential>(response));
        }

        Assert.InRange(credentials[0].Credential.ExpiresAt, DateTimeOffset.UtcNow.AddDays(30).AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(30).AddMinutes(1));
        using (var fourth = await CreateCredentialAsync(owner, organizationId, agent.Id, new { name = "C4", scopes = read }))
        {
            Assert.Equal(HttpStatusCode.Conflict, fourth.StatusCode);
        }

        var revokePath = $"/api/v1/organizations/{organizationId}/agents/{agent.Id}/credentials/{credentials[0].Credential.Id}/revoke";
        using (var revoke = await _kit.SendAsync(HttpMethod.Post, revokePath, owner))
        {
            var revoked = await ProjectTestKit.ReadAsync<AgentCredentialDetails>(revoke);
            Assert.Equal(AgentCredentialState.Revoked, revoked.State);
        }

        using (var again = await _kit.SendAsync(HttpMethod.Post, revokePath, owner))
        {
            Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        }

        using var replacement = await CreateCredentialAsync(owner, organizationId, agent.Id, new { name = "C4", scopes = read });
        Assert.Equal(HttpStatusCode.Created, replacement.StatusCode);
    }

    [Fact]
    public async Task ConcurrentCreations_NeverExceedThreeActiveCredentials()
    {
        var (owner, organizationId) = await CreateOwnerAsync();
        var agent = await CreateAgentAsync(owner, organizationId, "Agente concorrente");

        var responses = await Task.WhenAll(Enumerable.Range(1, 6).Select(i =>
            CreateCredentialAsync(owner, organizationId, agent.Id, new { name = $"C{i}", scopes = new[] { AgentScopes.TasksRead } })));
        try
        {
            foreach (var response in responses)
            {
                Assert.True(
                    response.StatusCode is HttpStatusCode.Created or HttpStatusCode.Conflict,
                    $"{response.StatusCode}: {await response.Content.ReadAsStringAsync(CancellationToken)}");
            }
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }

        await using var context = _database.CreateContext(organizationId);
        Assert.True(await context.AgentCredentials.CountAsync(c => c.AgentId == agent.Id, CancellationToken) <= AgentCredential.MaxActivePerAgent);
    }

    [Fact]
    public async Task ProjectAccess_IsContributorOrViewer_AndRequiresManagingTheProject()
    {
        var (owner, organizationId) = await CreateOwnerAsync();
        var project = await _kit.CreateProjectAsync(owner, organizationId);
        var agent = await CreateAgentAsync(owner, organizationId, "Agente de projeto");
        var accessPath = $"/api/v1/organizations/{organizationId}/agents/{agent.Id}/projects/{project.Id}";

        using (var manager = await _kit.SendAsync(HttpMethod.Put, accessPath, owner, new { role = "Manager" }))
        {
            Assert.Equal(HttpStatusCode.BadRequest, manager.StatusCode);
        }

        using (var grant = await _kit.SendAsync(HttpMethod.Put, accessPath, owner, new { role = "Contributor" }))
        {
            Assert.Equal(ProjectRole.Contributor, (await ProjectTestKit.ReadAsync<AgentProjectAccessDetails>(grant)).Role);
        }

        using (var change = await _kit.SendAsync(HttpMethod.Put, accessPath, owner, new { role = "Viewer" }))
        {
            Assert.Equal(ProjectRole.Viewer, (await ProjectTestKit.ReadAsync<AgentProjectAccessDetails>(change)).Role);
        }

        // Quem tem agents.manage, mas não gerencia o projeto: invisível (404), depois Colaborador (403), depois Gerente.
        var agentAdmin = await CreateMemberWithOnlyAgentsManageAsync(owner, organizationId);
        using (var invisible = await _kit.SendAsync(HttpMethod.Put, accessPath, agentAdmin, new { role = "Contributor" }))
        {
            Assert.Equal(HttpStatusCode.NotFound, invisible.StatusCode);
        }

        await _kit.AddToTeamAsync(owner, organizationId, project.Id, agentAdmin, "Contributor");
        using (var forbidden = await _kit.SendAsync(HttpMethod.Delete, accessPath, agentAdmin))
        {
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        }

        using (var promote = await _kit.SendToProjectAsync(HttpMethod.Patch, project.Id, $"/members/{agentAdmin.Id}", owner, organizationId, new { role = "Manager" }))
        {
            promote.EnsureSuccessStatusCode();
        }

        using (var removal = await _kit.SendAsync(HttpMethod.Delete, accessPath, agentAdmin))
        {
            Assert.Equal(HttpStatusCode.NoContent, removal.StatusCode);
        }

        using (var missing = await _kit.SendAsync(HttpMethod.Delete, accessPath, owner))
        {
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        }

        using var details = await _kit.SendAsync(HttpMethod.Get, $"/api/v1/organizations/{organizationId}/agents/{agent.Id}", agentAdmin);
        Assert.Empty((await ProjectTestKit.ReadAsync<AgentDetails>(details)).Projects);
    }

    [Fact]
    public async Task OnlyMembersWithAgentsManage_ReachTheRoutes()
    {
        var (owner, organizationId) = await CreateOwnerAsync();
        var agent = await CreateAgentAsync(owner, organizationId, "Agente protegido");
        var administrator = await Organizations.CreateUserAsync();
        var member = await Organizations.CreateUserAsync();
        var outsider = await Organizations.CreateUserAsync();
        await Organizations.AddMemberAsync(owner, organizationId, administrator, "Administrator");
        await Organizations.AddMemberAsync(owner, organizationId, member, "Member");
        var basePath = $"/api/v1/organizations/{organizationId}/agents";

        (HttpMethod Method, string Path, object? Body)[] routes =
        [
            (HttpMethod.Get, basePath, null),
            (HttpMethod.Post, basePath, new { name = "Outro" }),
            (HttpMethod.Get, $"{basePath}/{agent.Id}", null),
            (HttpMethod.Patch, $"{basePath}/{agent.Id}", new { name = "Renomeado" }),
            (HttpMethod.Post, $"{basePath}/{agent.Id}/disable", null),
            (HttpMethod.Post, $"{basePath}/{agent.Id}/enable", null),
            (HttpMethod.Get, $"{basePath}/{agent.Id}/credentials", null),
            (HttpMethod.Post, $"{basePath}/{agent.Id}/credentials", new { name = "X", scopes = new[] { AgentScopes.TasksRead } }),
            (HttpMethod.Post, $"{basePath}/{agent.Id}/credentials/{Guid.NewGuid()}/revoke", null),
            (HttpMethod.Put, $"{basePath}/{agent.Id}/projects/{Guid.NewGuid()}", new { role = "Viewer" }),
            (HttpMethod.Delete, $"{basePath}/{agent.Id}/projects/{Guid.NewGuid()}", null),
        ];

        foreach (var (method, path, body) in routes)
        {
            using var asMember = await _kit.SendAsync(method, path, member, body);
            using var asOutsider = await _kit.SendAsync(method, path, outsider, body);
            using var anonymous = await _kit.SendAsync(method, path, null, body);

            Assert.True(asMember.StatusCode == HttpStatusCode.Forbidden, $"{method} {path} (Membro): {asMember.StatusCode}");
            Assert.True(asOutsider.StatusCode == HttpStatusCode.NotFound, $"{method} {path} (de fora): {asOutsider.StatusCode}");
            Assert.True(anonymous.StatusCode == HttpStatusCode.Unauthorized, $"{method} {path} (anônimo): {anonymous.StatusCode}");
        }

        using var asAdministrator = await _kit.SendAsync(HttpMethod.Post, basePath, administrator, new { name = "Do administrador" });
        Assert.Equal(HttpStatusCode.Created, asAdministrator.StatusCode);
    }

    [Fact]
    public async Task AgentsAndProjects_OfAnotherOrganization_AreNotFound()
    {
        var (ownerA, organizationA) = await CreateOwnerAsync();
        var (ownerB, organizationB) = await CreateOwnerAsync();
        var agentA = await CreateAgentAsync(ownerA, organizationA, "Agente A");
        var agentB = await CreateAgentAsync(ownerB, organizationB, "Agente B");
        var projectA = await _kit.CreateProjectAsync(ownerA, organizationA);
        var pathB = $"/api/v1/organizations/{organizationB}/agents";

        (HttpMethod Method, string Path, object? Body)[] crossTenant =
        [
            (HttpMethod.Get, $"{pathB}/{agentA.Id}", null),
            (HttpMethod.Patch, $"{pathB}/{agentA.Id}", new { name = "Invasão" }),
            (HttpMethod.Post, $"{pathB}/{agentA.Id}/disable", null),
            (HttpMethod.Get, $"{pathB}/{agentA.Id}/credentials", null),
            (HttpMethod.Post, $"{pathB}/{agentA.Id}/credentials", new { name = "X", scopes = new[] { AgentScopes.TasksRead } }),
            (HttpMethod.Put, $"{pathB}/{agentA.Id}/projects/{projectA.Id}", new { role = "Viewer" }),
            (HttpMethod.Put, $"{pathB}/{agentB.Id}/projects/{projectA.Id}", new { role = "Viewer" }),
        ];

        foreach (var (method, path, body) in crossTenant)
        {
            using var response = await _kit.SendAsync(method, path, ownerB, body);
            Assert.True(response.StatusCode == HttpStatusCode.NotFound, $"{method} {path}: {response.StatusCode}");
        }

        using var listB = await _kit.SendAsync(HttpMethod.Get, pathB, ownerB);
        Assert.Equal([agentB.Id], (await ProjectTestKit.ReadAsync<List<AgentSummary>>(listB)).Select(a => a.Id));

        await using var context = _database.CreateContext(organizationA);
        var untouched = await context.Agents.SingleAsync(a => a.Id == agentA.Id, CancellationToken);
        Assert.Equal(("Agente A", AgentStatus.Active), (untouched.Name, untouched.Status));
        Assert.False(await context.AgentCredentials.AnyAsync(c => c.AgentId == agentA.Id, CancellationToken));
    }

    public void Dispose() => _factory.Dispose();

    private async Task<(TestUser Owner, Guid OrganizationId)> CreateOwnerAsync()
    {
        var owner = await Organizations.CreateUserAsync();
        var organization = await Organizations.CreateOrganizationAsync(owner);
        return (owner, organization.Id);
    }

    private async Task<AgentDetails> CreateAgentAsync(TestUser owner, Guid organizationId, string name, string? description = null)
    {
        using var response = await _kit.SendAsync(HttpMethod.Post, $"/api/v1/organizations/{organizationId}/agents", owner, new { name, description });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await ProjectTestKit.ReadAsync<AgentDetails>(response);
    }

    private Task<HttpResponseMessage> CreateCredentialAsync(TestUser owner, Guid organizationId, Guid agentId, object body) =>
        _kit.SendAsync(HttpMethod.Post, $"/api/v1/organizations/{organizationId}/agents/{agentId}/credentials", owner, body);

    /// <summary>
    /// Membro com um papel personalizado que só tem <c>organization.read</c> e <c>agents.manage</c>. Papéis
    /// personalizados ainda não têm endpoints (ADR-009), então o papel é criado direto no banco.
    /// </summary>
    private async Task<TestUser> CreateMemberWithOnlyAgentsManageAsync(TestUser owner, Guid organizationId)
    {
        var user = await Organizations.CreateUserAsync();
        await Organizations.AddMemberAsync(owner, organizationId, user, "Member");

        await using var connection = new NpgsqlConnection(_database.ConnectionString);
        await connection.OpenAsync(CancellationToken);
        await using var command = new NpgsqlCommand(
            """
            WITH role AS (
                INSERT INTO roles (id, organization_id, name, system_role, created_at, updated_at)
                VALUES (gen_random_uuid(), @organization, 'Gestor de agentes', NULL, now(), now())
                RETURNING id
            ), permissions AS (
                INSERT INTO role_permissions (role_id, permission_code, organization_id)
                SELECT role.id, code, @organization FROM role, unnest(ARRAY['organization.read', 'agents.manage']) AS code
            )
            UPDATE organization_members SET role_id = (SELECT id FROM role)
            WHERE organization_id = @organization AND user_id = @user
            """,
            connection);
        command.Parameters.AddWithValue("organization", organizationId);
        command.Parameters.AddWithValue("user", user.Id);
        Assert.Equal(1, await command.ExecuteNonQueryAsync(CancellationToken));

        return user;
    }

    /// <summary>Linhas de eventos (e, opcionalmente, de credenciais) cujo texto contém o valor informado.</summary>
    private async Task<long> CountRowsContainingAsync(string value, bool includeCredentials = true)
    {
        await using var connection = new NpgsqlConnection(_database.ConnectionString);
        await connection.OpenAsync(CancellationToken);
        var sql = "SELECT count(*) FROM activity_events e WHERE e::text LIKE '%' || @value || '%'";
        if (includeCredentials)
        {
            sql += " OR EXISTS (SELECT 1 FROM agent_credentials c WHERE c::text LIKE '%' || @value || '%')";
        }

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("value", value);
        return (long)(await command.ExecuteScalarAsync(CancellationToken))!;
    }
}
