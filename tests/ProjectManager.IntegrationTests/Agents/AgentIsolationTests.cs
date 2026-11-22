using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using ProjectManager.Domain.Agents;
using ProjectManager.IntegrationTests.Agents.Probes;
using ProjectManager.IntegrationTests.Infrastructure;
using ProjectManager.IntegrationTests.Organizations;
using ProjectManager.IntegrationTests.Projects;

namespace ProjectManager.IntegrationTests.Agents;

/// <summary>
/// Isolamento multi-tenant no comportamento real das requisições de agentes (revisão C2). Uma sonda com
/// <c>[AgentApi]</c> lê o banco com o contexto montado pelo pipeline verdadeiro (credencial, organização ativa e
/// filtros), e as rotas de pessoas são atacadas com credenciais e IDs da outra organização.
/// </summary>
public sealed class AgentIsolationTests : IAsyncLifetime
{
    private static readonly string[] Scopes = [AgentScopes.ContextRead, AgentScopes.TasksRead];

    private readonly PostgresDatabaseFixture _database;
    private readonly ApiFactory _factory;
    private readonly WebApplicationFactory<Program> _probes;
    private readonly AgentTestKit _kit;

    private Tenant _a = null!;
    private Tenant _b = null!;

    public AgentIsolationTests(PostgresDatabaseFixture database, MailpitFixture mailpit)
    {
        _database = database;
        _factory = new ApiFactory(database.ConnectionString, mailpit);
        _probes = _factory.WithProbes(typeof(AgentIsolationProbeController));
        _kit = new AgentTestKit(_probes.CreateClient(), mailpit);
    }

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _a = await CreateTenantAsync("A");
        _b = await CreateTenantAsync("B");
    }

    [Fact]
    public async Task AgentRequest_SeesOnlyTheDataOfItsCredentialOrganization()
    {
        foreach (var (own, other) in new[] { (_a, _b), (_b, _a) })
        {
            using var response = await _kit.SendAsAgentAsync(own.Token, HttpMethod.Get, "/api/v1/agents/__probe/visible");
            var visible = await ProjectTestKit.ReadAsync<VisibleData>(response);

            Assert.Equal(own.OrganizationId, visible.OrganizationId);
            Assert.Equal([own.OrganizationId], visible.Organizations);
            Assert.Equal([own.AgentId], visible.Agents);
            Assert.Equal([own.CredentialId], visible.Credentials);
            Assert.Equal([own.GrantedProjectId], visible.GrantedProjects);
            Assert.DoesNotContain(other.GrantedProjectId, visible.Projects);
            Assert.DoesNotContain(other.UngrantedProjectId, visible.Projects);
        }
    }

    [Fact]
    public async Task CredentialOfAnotherOrganization_IsNotFoundById()
    {
        using var foreign = await _kit.SendAsAgentAsync(_a.Token, HttpMethod.Get, $"/api/v1/agents/__probe/credentials/{_b.CredentialId}");
        using var own = await _kit.SendAsAgentAsync(_a.Token, HttpMethod.Get, $"/api/v1/agents/__probe/credentials/{_a.CredentialId}");

        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.Equal(HttpStatusCode.OK, own.StatusCode);
    }

    [Fact]
    public async Task OrganizationInRouteOrHeader_MustMatchTheCredential()
    {
        var a = _a.OrganizationId.ToString();
        var b = _b.OrganizationId.ToString();

        (string Path, string[]? Header, HttpStatusCode Expected)[] cases =
        [
            ($"/api/v1/agents/__probe/organizations/{a}/visible", null, HttpStatusCode.OK),
            ($"/api/v1/agents/__probe/organizations/{b}/visible", null, HttpStatusCode.NotFound),
            ($"/api/v1/agents/__probe/organizations/{b}/visible", [a], HttpStatusCode.NotFound),
            ($"/api/v1/agents/__probe/organizations/{a}/visible", [b], HttpStatusCode.NotFound),
            ("/api/v1/agents/__probe/visible", [a.ToUpperInvariant()], HttpStatusCode.OK),
            ("/api/v1/agents/__probe/visible", [b], HttpStatusCode.NotFound),
            ("/api/v1/agents/__probe/visible", [a, b], HttpStatusCode.NotFound),
            ("/api/v1/agents/__probe/visible", [a, a], HttpStatusCode.NotFound),
            ("/api/v1/agents/__probe/visible", ["não-é-um-guid"], HttpStatusCode.NotFound),
            ("/api/v1/agents/__probe/visible", [Guid.Empty.ToString()], HttpStatusCode.NotFound),
        ];

        foreach (var (path, header, expected) in cases)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(path, UriKind.Relative));
            request.Headers.Authorization = new("Bearer", _a.Token);
            if (header is not null)
            {
                request.Headers.TryAddWithoutValidation(ProjectTestKit.OrganizationHeader, header);
            }

            using var response = await _probes.CreateClient().SendAsync(request, CancellationToken);
            Assert.True(response.StatusCode == expected, $"{path} com cabeçalho [{string.Join(", ", header ?? [])}]: {response.StatusCode}");
        }
    }

    [Fact]
    public async Task ValidCredential_DoesNotReachPeopleRoutesOfAnyOrganization()
    {
        string[] paths =
        [
            $"/api/v1/organizations/{_a.OrganizationId}/agents",
            $"/api/v1/organizations/{_b.OrganizationId}/agents",
            $"/api/v1/organizations/{_b.OrganizationId}/agents/{_b.AgentId}/credentials",
        ];

        foreach (var path in paths)
        {
            using var response = await _kit.SendAsAgentAsync(_a.Token, HttpMethod.Get, path);
            Assert.True(response.StatusCode == HttpStatusCode.Unauthorized, $"{path}: {response.StatusCode}");
        }
    }

    [Fact]
    public async Task AnotherOrganization_CannotRevokeOrDisableTheCredentialOrTheAgent()
    {
        var credentialPath = $"/{_a.AgentId}/credentials/{_a.CredentialId}/revoke";
        using (var revoke = await _kit.ManageAsync(HttpMethod.Post, _b.Owner, _b.OrganizationId, credentialPath))
        using (var disable = await _kit.ManageAsync(HttpMethod.Post, _b.Owner, _b.OrganizationId, $"/{_a.AgentId}/disable"))
        using (var list = await _kit.ManageAsync(HttpMethod.Get, _b.Owner, _b.OrganizationId, $"/{_a.AgentId}/credentials"))
        {
            Assert.Equal(HttpStatusCode.NotFound, revoke.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, disable.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, list.StatusCode);
        }

        using var stillValid = await _kit.SendAsAgentAsync(_a.Token, HttpMethod.Get, "/api/v1/agents/me");
        Assert.Equal(HttpStatusCode.OK, stillValid.StatusCode);

        await using var context = _database.CreateContext(_a.OrganizationId);
        var credential = await context.AgentCredentials.SingleAsync(c => c.Id == _a.CredentialId, CancellationToken);
        var agent = await context.Agents.SingleAsync(x => x.Id == _a.AgentId, CancellationToken);
        Assert.Null(credential.RevokedAt);
        Assert.Equal(AgentStatus.Active, agent.Status);
    }

    /// <summary>
    /// Não há rotas de projeto para agentes antes da etapa 7: hoje, nenhum projeto é alcançável com a credencial, nem
    /// o concedido. O acesso por concessão (<c>AgentAccessGrant</c>) é testado nas etapas 5 e 7.
    /// </summary>
    [Fact]
    public async Task ProjectsWithOrWithoutGrant_AreNotReachableThroughPeopleRoutes()
    {
        Guid[] projects = [_a.GrantedProjectId, _a.UngrantedProjectId, _b.GrantedProjectId];

        foreach (var projectId in projects)
        {
            foreach (var subPath in new[] { string.Empty, "/tasks", "/activities" })
            {
                using var response = await _kit.SendAsAgentAsync(
                    _a.Token,
                    HttpMethod.Get,
                    $"/api/v1/projects/{projectId}{subPath}",
                    headers: new Dictionary<string, string> { [ProjectTestKit.OrganizationHeader] = _a.OrganizationId.ToString() });
                Assert.True(response.StatusCode == HttpStatusCode.Unauthorized, $"{projectId}{subPath}: {response.StatusCode}");
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        _probes.Dispose();
        _factory.Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task<Tenant> CreateTenantAsync(string label)
    {
        var (owner, organizationId) = await _kit.CreateOwnerAsync();
        var agent = await _kit.CreateAgentAsync(owner, organizationId, $"Agente {label}");
        var credential = await _kit.CreateCredentialAsync(owner, organizationId, agent.Id, Scopes);
        var granted = await _kit.Projects.CreateProjectAsync(owner, organizationId, new { name = $"Concedido {label}" });
        var ungranted = await _kit.Projects.CreateProjectAsync(owner, organizationId, new { name = $"Sem concessão {label}" });
        using (var grant = await _kit.ManageAsync(HttpMethod.Put, owner, organizationId, $"/{agent.Id}/projects/{granted.Id}", new { role = "Contributor" }))
        {
            grant.EnsureSuccessStatusCode();
        }

        return new Tenant(owner, organizationId, agent.Id, credential.Credential.Id, credential.Token, granted.Id, ungranted.Id);
    }

    private sealed record Tenant(
        TestUser Owner,
        Guid OrganizationId,
        Guid AgentId,
        Guid CredentialId,
        string Token,
        Guid GrantedProjectId,
        Guid UngrantedProjectId);
}
