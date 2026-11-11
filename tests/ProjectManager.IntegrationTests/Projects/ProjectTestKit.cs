using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using ProjectManager.Application.Projects;
using ProjectManager.IntegrationTests.Infrastructure;
using ProjectManager.IntegrationTests.Organizations;

namespace ProjectManager.IntegrationTests.Projects;

/// <summary>
/// Operações de projeto pela API com usuários reais. As rotas por ID do projeto levam o cabeçalho
/// <c>X-Gestalyn-Organization</c>.
/// </summary>
internal sealed class ProjectTestKit(HttpClient client, MailpitFixture mailpit)
{
    public const string OrganizationHeader = "X-Gestalyn-Organization";

    /// <summary>As enumerações chegam pelo nome, como a API as envia.</summary>
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public OrganizationTestKit Organizations { get; } = new(client, mailpit);

    public async Task<ProjectDetails> CreateProjectAsync(TestUser user, Guid organizationId, object? body = null)
    {
        using var response = await CreateAsync(user, organizationId, body ?? new { name = "Projeto de teste" });
        response.EnsureSuccessStatusCode();

        return await ReadAsync<ProjectDetails>(response);
    }

    public Task<HttpResponseMessage> CreateAsync(TestUser user, Guid organizationId, object body) =>
        SendAsync(HttpMethod.Post, $"/api/v1/organizations/{organizationId}/projects", user, body);

    public Task<HttpResponseMessage> ListAsync(TestUser user, Guid organizationId, string query = "") =>
        SendAsync(HttpMethod.Get, $"/api/v1/organizations/{organizationId}/projects{query}", user);

    /// <summary>Requisição numa rota por ID do projeto, com a organização no cabeçalho.</summary>
    public Task<HttpResponseMessage> SendToProjectAsync(
        HttpMethod method,
        Guid projectId,
        string subPath,
        TestUser user,
        Guid organizationId,
        object? body = null) =>
        SendAsync(method, $"/api/v1/projects/{projectId}{subPath}", user, body, organizationId.ToString());

    public async Task AddToTeamAsync(TestUser manager, Guid organizationId, Guid projectId, TestUser user, string role, bool isResponsible = false)
    {
        using var response = await SendToProjectAsync(
            HttpMethod.Post,
            projectId,
            "/members",
            manager,
            organizationId,
            new { userId = user.Id, role, isResponsible });
        response.EnsureSuccessStatusCode();
    }

    public async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, TestUser? user, object? body = null, string? organizationHeader = null)
    {
        using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative));
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        if (user is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", user.AccessToken);
        }

        if (organizationHeader is not null)
        {
            request.Headers.TryAddWithoutValidation(OrganizationHeader, organizationHeader);
        }

        return await client.SendAsync(request, CancellationToken);
    }

    public static async Task<T> ReadAsync<T>(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<T>(Json, CancellationToken))!;
}
