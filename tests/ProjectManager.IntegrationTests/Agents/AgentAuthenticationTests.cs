using System.Net;
using System.Text.Json;
using ProjectManager.Application.Agents;
using ProjectManager.Domain.Agents;
using ProjectManager.IntegrationTests.Infrastructure;
using ProjectManager.IntegrationTests.Projects;

namespace ProjectManager.IntegrationTests.Agents;

/// <summary>
/// Autenticação de agentes (ADR-016): credencial própria, sem sessão de usuário; cada esquema só nas suas rotas;
/// revogação, desativação e expiração na requisição seguinte, sempre com o mesmo 401; organização só da credencial.
/// </summary>
public sealed class AgentAuthenticationTests : IDisposable
{
    private const string MePath = "/api/v1/agents/me";

    private static readonly string[] ReadScopes = [AgentScopes.TasksRead, AgentScopes.ContextRead];

    private readonly PostgresDatabaseFixture _database;
    private readonly MailpitFixture _mailpit;
    private readonly ApiFactory _factory;
    private readonly AgentTestKit _kit;

    public AgentAuthenticationTests(PostgresDatabaseFixture database, MailpitFixture mailpit)
    {
        _database = database;
        _mailpit = mailpit;
        _factory = new ApiFactory(database.ConnectionString, mailpit);
        _kit = new AgentTestKit(_factory.CreateClient(), mailpit);
    }

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Token_AuthenticatesTheAgentWithoutAnyUserSession()
    {
        var (owner, organizationId) = await _kit.CreateOwnerAsync();
        var agent = await _kit.CreateAgentAsync(owner, organizationId, "Agente autenticado");
        var credential = await _kit.CreateCredentialAsync(owner, organizationId, agent.Id, ReadScopes);

        using var response = await _kit.SendAsAgentAsync(credential.Token, HttpMethod.Get, MePath);
        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        var me = JsonSerializer.Deserialize<AgentMe>(body, ProjectTestKit.Json)!;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal((agent.Id, "Agente autenticado", organizationId), (me.AgentId, me.Name, me.OrganizationId));
        Assert.Equal((credential.Credential.Id, credential.Credential.Prefix), (me.Credential.Id, me.Credential.Prefix));
        Assert.Equal([AgentScopes.ContextRead, AgentScopes.TasksRead], me.Credential.Scopes);
        Assert.DoesNotContain(credential.Token[AgentTokens.Marker.Length..], body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EachScheme_WorksOnlyOnItsOwnRoutes()
    {
        var (owner, organizationId) = await _kit.CreateOwnerAsync();
        var agent = await _kit.CreateAgentAsync(owner, organizationId, "Agente separado");
        var credential = await _kit.CreateCredentialAsync(owner, organizationId, agent.Id, ReadScopes);

        using var userOnAgentRoute = await _kit.SendAsAgentAsync(owner.AccessToken, HttpMethod.Get, MePath);
        using var agentOnUserRoutes = await _kit.SendAsAgentAsync(credential.Token, HttpMethod.Get, "/api/v1/organizations");
        using var agentOnOrganization = await _kit.SendAsAgentAsync(credential.Token, HttpMethod.Get, $"/api/v1/organizations/{organizationId}/agents");
        using var anonymous = await _kit.Projects.SendAsync(HttpMethod.Get, MePath, user: null);

        Assert.Equal(HttpStatusCode.Unauthorized, userOnAgentRoute.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, agentOnUserRoutes.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, agentOnOrganization.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal("Bearer", anonymous.Headers.WwwAuthenticate.ToString());
    }

    [Fact]
    public async Task RevocationAndDisabling_TakeEffectOnTheNextRequest_WithTheSameResponseAsAnUnknownToken()
    {
        var (owner, organizationId) = await _kit.CreateOwnerAsync();
        var agent = await _kit.CreateAgentAsync(owner, organizationId, "Agente revogável");
        var first = await _kit.CreateCredentialAsync(owner, organizationId, agent.Id, ReadScopes);
        var second = await _kit.CreateCredentialAsync(owner, organizationId, agent.Id, ReadScopes);

        Assert.Equal(HttpStatusCode.OK, await StatusAsync(first.Token));

        using (var revoke = await _kit.ManageAsync(HttpMethod.Post, owner, organizationId, $"/{agent.Id}/credentials/{first.Credential.Id}/revoke"))
        {
            revoke.EnsureSuccessStatusCode();
        }

        var revoked = await ProblemAsync(first.Token);
        Assert.Equal(HttpStatusCode.OK, await StatusAsync(second.Token));

        using (var disable = await _kit.ManageAsync(HttpMethod.Post, owner, organizationId, $"/{agent.Id}/disable"))
        {
            disable.EnsureSuccessStatusCode();
        }

        var disabled = await ProblemAsync(second.Token);

        using (var enable = await _kit.ManageAsync(HttpMethod.Post, owner, organizationId, $"/{agent.Id}/enable"))
        {
            enable.EnsureSuccessStatusCode();
        }

        Assert.Equal(HttpStatusCode.OK, await StatusAsync(second.Token));
        Assert.Equal(HttpStatusCode.Unauthorized, await StatusAsync(first.Token));

        var unknown = await ProblemAsync(AgentTokens.Marker + new string('Q', AgentTokens.RandomPartLength));
        Assert.Equal(unknown, revoked);
        Assert.Equal(unknown, disabled);
        Assert.Equal((HttpStatusCode.Unauthorized, "Bearer"), (unknown.Status, unknown.Challenge));

        Assert.DoesNotContain(_factory.Logs.Entries, entry =>
            entry.Contains(first.Token[AgentTokens.Marker.Length..], StringComparison.Ordinal)
            || entry.Contains(second.Token[AgentTokens.Marker.Length..], StringComparison.Ordinal)
            || entry.Contains(first.Credential.Prefix, StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExpiredCredential_IsRejected()
    {
        var clock = new ManualTimeProvider(DateTimeOffset.UtcNow);
        using var factory = new ApiFactory(_database.ConnectionString, _mailpit, timeProvider: clock);
        var kit = new AgentTestKit(factory.CreateClient(), _mailpit);
        var (owner, organizationId) = await kit.CreateOwnerAsync();
        var agent = await kit.CreateAgentAsync(owner, organizationId, "Agente temporário");
        var credential = await kit.CreateCredentialAsync(owner, organizationId, agent.Id, ReadScopes, expiresInDays: 1);

        using (var valid = await kit.SendAsAgentAsync(credential.Token, HttpMethod.Get, MePath))
        {
            Assert.Equal(HttpStatusCode.OK, valid.StatusCode);
        }

        clock.UtcNow = credential.Credential.ExpiresAt.AddTicks(-10);
        using (var lastMoment = await kit.SendAsAgentAsync(credential.Token, HttpMethod.Get, MePath))
        {
            Assert.Equal(HttpStatusCode.OK, lastMoment.StatusCode);
        }

        clock.UtcNow = credential.Credential.ExpiresAt;
        using var expired = await kit.SendAsAgentAsync(credential.Token, HttpMethod.Get, MePath);
        Assert.Equal(HttpStatusCode.Unauthorized, expired.StatusCode);
    }

    /// <summary>
    /// Revisão C3: token desconhecido, malformado, revogado, expirado e de agente desativado produzem respostas
    /// idênticas (status, cabeçalhos e corpo, exceto o ID de rastreio), sem nada que ajude a enumerar credenciais.
    /// </summary>
    [Fact]
    public async Task EveryRejection_IsIndistinguishable_AndRevealsNothingAboutTheCredential()
    {
        var clock = new ManualTimeProvider(DateTimeOffset.UtcNow);
        using var factory = new ApiFactory(_database.ConnectionString, _mailpit, timeProvider: clock);
        var kit = new AgentTestKit(factory.CreateClient(), _mailpit);
        var (owner, organizationId) = await kit.CreateOwnerAsync();
        var active = await kit.CreateAgentAsync(owner, organizationId, "Agente com credenciais");
        var disabledAgent = await kit.CreateAgentAsync(owner, organizationId, "Agente desativado");
        var revoked = await kit.CreateCredentialAsync(owner, organizationId, active.Id, ReadScopes);
        var expiring = await kit.CreateCredentialAsync(owner, organizationId, active.Id, ReadScopes, expiresInDays: 1);
        var ofDisabled = await kit.CreateCredentialAsync(owner, organizationId, disabledAgent.Id, ReadScopes);

        using (var revoke = await kit.ManageAsync(HttpMethod.Post, owner, organizationId, $"/{active.Id}/credentials/{revoked.Credential.Id}/revoke"))
        using (var disable = await kit.ManageAsync(HttpMethod.Post, owner, organizationId, $"/{disabledAgent.Id}/disable"))
        {
            revoke.EnsureSuccessStatusCode();
            disable.EnsureSuccessStatusCode();
        }

        clock.UtcNow = expiring.Credential.ExpiresAt;

        var cases = new Dictionary<string, string>
        {
            ["desconhecido"] = AgentTokens.Marker + new string('Q', AgentTokens.RandomPartLength),
            ["malformado"] = "gsk_curto",
            ["revogado"] = revoked.Token,
            ["expirado"] = expiring.Token,
            ["agente desativado"] = ofDisabled.Token,
        };

        var responses = new Dictionary<string, (RawRejection Shape, string Body)>();
        foreach (var (name, token) in cases)
        {
            using var response = await kit.SendAsAgentAsync(token, HttpMethod.Get, MePath);
            var body = await response.Content.ReadAsStringAsync(CancellationToken);
            responses[name] = (RawRejection.From(response, body), body);
        }

        var reference = responses["desconhecido"].Shape;
        Assert.Equal(HttpStatusCode.Unauthorized, reference.Status);
        foreach (var (name, (shape, body)) in responses)
        {
            Assert.True(shape == reference, $"'{name}' difere de 'desconhecido': {shape} | {reference}");

            string[] secrets =
            [
                active.Id.ToString(), disabledAgent.Id.ToString(), organizationId.ToString(),
                revoked.Credential.Id.ToString(), expiring.Credential.Id.ToString(), ofDisabled.Credential.Id.ToString(),
                revoked.Credential.Prefix, expiring.Credential.Prefix, ofDisabled.Credential.Prefix,
            ];
            string[] hints = ["revog", "expir", "desativ", "desconhec", "disabled", "revoked", "expired", "unknown"];
            Assert.DoesNotContain(secrets, secret => body.Contains(secret, StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(hints, hint => body.Contains(hint, StringComparison.OrdinalIgnoreCase));
        }
    }

    [Theory]
    [InlineData("gsk_curto")]
    [InlineData("gsk_ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmno+q")]
    [InlineData("gsk_")]
    public async Task MalformedTokens_AreRejected(string token)
    {
        using var response = await _kit.SendAsAgentAsync(token, HttpMethod.Get, MePath);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task TamperedToken_IsRejected()
    {
        var (owner, organizationId) = await _kit.CreateOwnerAsync();
        var agent = await _kit.CreateAgentAsync(owner, organizationId, "Agente adulterado");
        var credential = await _kit.CreateCredentialAsync(owner, organizationId, agent.Id, ReadScopes);
        var last = credential.Token[^1];
        var tampered = credential.Token[..^1] + (last == 'A' ? 'B' : 'A');

        Assert.Equal(HttpStatusCode.Unauthorized, await StatusAsync(tampered));
        Assert.Equal(HttpStatusCode.OK, await StatusAsync(credential.Token));
    }

    [Fact]
    public async Task Organization_ComesOnlyFromTheCredential()
    {
        var (ownerA, organizationA) = await _kit.CreateOwnerAsync();
        var (ownerB, organizationB) = await _kit.CreateOwnerAsync();
        var agentA = await _kit.CreateAgentAsync(ownerA, organizationA, "Agente A");
        var agentB = await _kit.CreateAgentAsync(ownerB, organizationB, "Agente B");
        var tokenA = (await _kit.CreateCredentialAsync(ownerA, organizationA, agentA.Id, ReadScopes)).Token;
        var tokenB = (await _kit.CreateCredentialAsync(ownerB, organizationB, agentB.Id, ReadScopes)).Token;

        Assert.Equal(HttpStatusCode.OK, await StatusAsync(tokenA, organizationA.ToString()));
        Assert.Equal(HttpStatusCode.NotFound, await StatusAsync(tokenA, organizationB.ToString()));
        Assert.Equal(HttpStatusCode.NotFound, await StatusAsync(tokenA, "não-é-um-guid"));

        using var meB = await _kit.SendAsAgentAsync(tokenB, HttpMethod.Get, MePath);
        Assert.Equal((agentB.Id, organizationB), await ReadIdentityAsync(meB));
    }

    public void Dispose() => _factory.Dispose();

    private static async Task<(Guid AgentId, Guid OrganizationId)> ReadIdentityAsync(HttpResponseMessage response)
    {
        var me = await ProjectTestKit.ReadAsync<AgentMe>(response);
        return (me.AgentId, me.OrganizationId);
    }

    private async Task<HttpStatusCode> StatusAsync(string token, string? organizationHeader = null)
    {
        var headers = organizationHeader is null ? null : new Dictionary<string, string> { [ProjectTestKit.OrganizationHeader] = organizationHeader };
        using var response = await _kit.SendAsAgentAsync(token, HttpMethod.Get, MePath, headers: headers);
        return response.StatusCode;
    }

    /// <summary>O que distingue uma recusa para o cliente: status, título, detalhe e desafio (sem o ID de rastreio).</summary>
    private async Task<RejectionShape> ProblemAsync(string token)
    {
        using var response = await _kit.SendAsAgentAsync(token, HttpMethod.Get, MePath);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        string? Text(string name) => problem.RootElement.TryGetProperty(name, out var value) ? value.GetString() : null;

        return new RejectionShape(response.StatusCode, Text("title"), Text("detail"), response.Headers.WwwAuthenticate.ToString());
    }

    private sealed record RejectionShape(HttpStatusCode Status, string? Title, string? Detail, string Challenge);

    /// <summary>A resposta inteira, normalizada: status, todos os cabeçalhos (menos a data) e o corpo sem o ID de rastreio.</summary>
    private sealed record RawRejection(HttpStatusCode Status, string Headers, string Body)
    {
        public static RawRejection From(HttpResponseMessage response, string body)
        {
            var headers = response.Headers.Concat(response.Content.Headers)
                .Where(header => !header.Key.Equals("Date", StringComparison.OrdinalIgnoreCase)
                    && !header.Key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
                .Select(header => $"{header.Key}={string.Join(",", header.Value)}")
                .Order(StringComparer.Ordinal);

            using var document = JsonDocument.Parse(body);
            var fields = document.RootElement.EnumerateObject()
                .Where(property => property.Name != "traceId")
                .Select(property => $"{property.Name}={property.Value}")
                .Order(StringComparer.Ordinal);

            return new RawRejection(response.StatusCode, string.Join(";", headers), string.Join(";", fields));
        }
    }
}
