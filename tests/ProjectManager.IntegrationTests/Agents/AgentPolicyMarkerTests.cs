using System.Net;
using ProjectManager.API.Agents;
using ProjectManager.Domain.Agents;
using ProjectManager.IntegrationTests.Agents.Probes;
using ProjectManager.IntegrationTests.Infrastructure;

namespace ProjectManager.IntegrationTests.Agents;

/// <summary>
/// Política por marca explícita (ADR-016, revisão C1): <c>[AgentApi]</c>, e não o namespace, decide entre as políticas
/// Agent e User, e a marca é conferida contra a rota. Uma configuração incoerente impede a API de iniciar.
/// </summary>
public sealed class AgentPolicyMarkerTests(PostgresDatabaseFixture database, MailpitFixture mailpit) : IDisposable
{
    private static readonly string[] Scopes = [AgentScopes.ContextRead];

    private readonly ApiFactory _factory = new(database.ConnectionString, mailpit);

    [Fact]
    public async Task MarkedControllerOutsideTheConventionalNamespace_UsesTheAgentPolicy()
    {
        using var probes = _factory.WithProbes(typeof(AgentIsolationProbeController));
        var kit = new AgentTestKit(probes.CreateClient(), mailpit);
        var (owner, organizationId) = await kit.CreateOwnerAsync();
        var agent = await kit.CreateAgentAsync(owner, organizationId, "Agente da sonda");
        var token = (await kit.CreateCredentialAsync(owner, organizationId, agent.Id, Scopes)).Token;

        foreach (var path in new[] { "/api/v1/agents/__probe/visible", "/api/v1/agents/__probe-absolute" })
        {
            using var asAgent = await kit.SendAsAgentAsync(token, HttpMethod.Get, path);
            using var asUser = await kit.SendAsAgentAsync(owner.AccessToken, HttpMethod.Get, path);

            Assert.True(asAgent.StatusCode == HttpStatusCode.OK, $"{path} (agente): {asAgent.StatusCode}");
            Assert.True(asUser.StatusCode == HttpStatusCode.Unauthorized, $"{path} (pessoa): {asUser.StatusCode}");
        }
    }

    [Fact]
    public async Task UnmarkedControllers_UseTheUserPolicy_EvenInTheAgentsNamespace()
    {
        using var probes = _factory.WithProbes(typeof(UserProbeController), typeof(UserProbeInAgentsNamespaceController));
        var kit = new AgentTestKit(probes.CreateClient(), mailpit);
        var (owner, organizationId) = await kit.CreateOwnerAsync();
        var agent = await kit.CreateAgentAsync(owner, organizationId, "Agente barrado");
        var token = (await kit.CreateCredentialAsync(owner, organizationId, agent.Id, Scopes)).Token;

        foreach (var path in new[] { "/api/v1/__probe/user", "/api/v1/__probe/user-in-agents-namespace" })
        {
            using var asUser = await kit.SendAsAgentAsync(owner.AccessToken, HttpMethod.Get, path);
            using var asAgent = await kit.SendAsAgentAsync(token, HttpMethod.Get, path);

            Assert.True(asUser.StatusCode == HttpStatusCode.OK, $"{path} (pessoa): {asUser.StatusCode}");
            Assert.True(asAgent.StatusCode == HttpStatusCode.Unauthorized, $"{path} (agente): {asAgent.StatusCode}");
        }
    }

    [Theory]
    [InlineData(typeof(MarkedOutsidePrefixProbeController), "fora de")]
    [InlineData(typeof(MarkedWithEscapingActionProbeController), "fora de")]
    [InlineData(typeof(UnmarkedAgentRouteProbeController), "não tem [AgentApi]")]
    [InlineData(typeof(UnmarkedActionRouteProbeController), "não tem [AgentApi]")]
    public void IncoherentMarkOrRoute_PreventsTheApiFromStarting(Type controller, string expectedMessage)
    {
        using var probes = _factory.WithProbes(controller);

        var exception = Assert.ThrowsAny<Exception>(() => probes.CreateClient());

        var error = Unwrap(exception);
        Assert.IsType<InvalidOperationException>(error);
        Assert.Contains(controller.FullName!, error.Message, StringComparison.Ordinal);
        Assert.Contains(expectedMessage, error.Message, StringComparison.Ordinal);
    }

    public void Dispose() => _factory.Dispose();

    /// <summary>A inicialização pode embrulhar o erro da convenção; o que importa é a causa.</summary>
    private static Exception Unwrap(Exception exception)
    {
        var current = exception;
        while (current is not InvalidOperationException && current.InnerException is not null)
        {
            current = current.InnerException;
        }

        return current;
    }
}
