using System.Net.Http.Headers;
using ProjectManager.Application.Activity;
using ProjectManager.Application.Common;
using ProjectManager.IntegrationTests.Infrastructure;
using ProjectManager.IntegrationTests.Organizations;
using ProjectManager.IntegrationTests.Projects;
using ProjectManager.IntegrationTests.Tasks;

namespace ProjectManager.IntegrationTests.Activity;

/// <summary>Leitura de atividades pela API, nos dois níveis.</summary>
internal sealed class ActivityTestKit(HttpClient client, MailpitFixture mailpit)
{
    public const string ClientHeader = "X-Gestalyn-Client";

    public static readonly byte[] PngBytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52];

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public TaskTestKit Tasks { get; } = new(client, mailpit);

    public ProjectTestKit Projects => Tasks.Projects;

    public OrganizationTestKit Organizations => Tasks.Organizations;

    public async Task<PagedResult<ActivityItem>> ListOrganizationAsync(TestUser user, Guid organizationId, string query = "")
    {
        using var response = await Projects.SendAsync(HttpMethod.Get, $"/api/v1/organizations/{organizationId}/activities{query}", user);
        response.EnsureSuccessStatusCode();
        return await ProjectTestKit.ReadAsync<PagedResult<ActivityItem>>(response);
    }

    public async Task<PagedResult<ActivityItem>> ListProjectAsync(TestUser user, Guid organizationId, Guid projectId, string query = "")
    {
        using var response = await Projects.SendToProjectAsync(HttpMethod.Get, projectId, $"/activities{query}", user, organizationId);
        response.EnsureSuccessStatusCode();
        return await ProjectTestKit.ReadAsync<PagedResult<ActivityItem>>(response);
    }

    /// <summary>Requisição com o cabeçalho de origem informado (ou sem ele, com <c>null</c>).</summary>
    public async Task<HttpResponseMessage> SendWithClientAsync(HttpMethod method, string path, TestUser user, Guid organizationId, object? body, string? clientName)
    {
        using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative));
        if (body is not null)
        {
            request.Content = System.Net.Http.Json.JsonContent.Create(body);
        }

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", user.AccessToken);
        request.Headers.TryAddWithoutValidation(ProjectTestKit.OrganizationHeader, organizationId.ToString());
        if (clientName is not null)
        {
            request.Headers.TryAddWithoutValidation(ClientHeader, clientName);
        }

        return await client.SendAsync(request, CancellationToken);
    }

    public async Task<HttpResponseMessage> UploadAsync(Guid taskId, Guid organizationId, TestUser user, string fileName, byte[] content)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "file", fileName);

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"/api/v1/tasks/{taskId}/attachments", UriKind.Relative)) { Content = form };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", user.AccessToken);
        request.Headers.TryAddWithoutValidation(ProjectTestKit.OrganizationHeader, organizationId.ToString());

        return await client.SendAsync(request, CancellationToken);
    }
}
