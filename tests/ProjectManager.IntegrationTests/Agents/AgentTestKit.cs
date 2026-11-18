using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ProjectManager.Application.Agents;
using ProjectManager.IntegrationTests.Infrastructure;
using ProjectManager.IntegrationTests.Organizations;
using ProjectManager.IntegrationTests.Projects;

namespace ProjectManager.IntegrationTests.Agents;

/// <summary>
/// Agentes reais pela API: uma pessoa com <c>agents.manage</c> cria o agente e a credencial, e o token emitido é usado
/// nas rotas de agentes como um cliente de verdade.
/// </summary>
internal sealed class AgentTestKit(HttpClient client, MailpitFixture mailpit)
{
    public ProjectTestKit Projects { get; } = new(client, mailpit);

    public OrganizationTestKit Organizations => Projects.Organizations;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public async Task<(TestUser Owner, Guid OrganizationId)> CreateOwnerAsync()
    {
        var owner = await Organizations.CreateUserAsync();
        var organization = await Organizations.CreateOrganizationAsync(owner);
        return (owner, organization.Id);
    }

    public async Task<AgentDetails> CreateAgentAsync(TestUser owner, Guid organizationId, string name)
    {
        using var response = await Projects.SendAsync(HttpMethod.Post, AgentsPath(organizationId), owner, new { name });
        await EnsureCreatedAsync(response);
        return await ProjectTestKit.ReadAsync<AgentDetails>(response);
    }

    public async Task<CreatedAgentCredential> CreateCredentialAsync(
        TestUser owner,
        Guid organizationId,
        Guid agentId,
        IReadOnlyList<string> scopes,
        int? expiresInDays = null)
    {
        using var response = await Projects.SendAsync(
            HttpMethod.Post,
            $"{AgentsPath(organizationId)}/{agentId}/credentials",
            owner,
            new { name = "Credencial de teste", scopes, expiresInDays });
        await EnsureCreatedAsync(response);
        return await ProjectTestKit.ReadAsync<CreatedAgentCredential>(response);
    }

    public async Task<HttpResponseMessage> ManageAsync(HttpMethod method, TestUser owner, Guid organizationId, string subPath, object? body = null) =>
        await Projects.SendAsync(method, AgentsPath(organizationId) + subPath, owner, body);

    /// <summary>Requisição de agente: <c>Authorization: Bearer gsk_...</c> e, opcionalmente, outros cabeçalhos.</summary>
    public async Task<HttpResponseMessage> SendAsAgentAsync(
        string token,
        HttpMethod method,
        string path,
        object? body = null,
        IReadOnlyDictionary<string, string>? headers = null)
    {
        using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        foreach (var (name, value) in headers ?? new Dictionary<string, string>())
        {
            request.Headers.TryAddWithoutValidation(name, value);
        }

        return await client.SendAsync(request, CancellationToken);
    }

    /// <summary>Falha com o status e o corpo da resposta, para o diagnóstico não depender de repetir o teste.</summary>
    private static async Task EnsureCreatedAsync(HttpResponseMessage response) =>
        Assert.True(
            response.StatusCode == HttpStatusCode.Created,
            $"Esperado 201, recebido {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(CancellationToken)}");

    private static string AgentsPath(Guid organizationId) => $"/api/v1/organizations/{organizationId}/agents";
}
