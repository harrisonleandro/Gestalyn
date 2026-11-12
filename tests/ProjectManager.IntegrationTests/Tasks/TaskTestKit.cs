using ProjectManager.Application.Projects;
using ProjectManager.Application.Tasks;
using ProjectManager.IntegrationTests.Infrastructure;
using ProjectManager.IntegrationTests.Organizations;
using ProjectManager.IntegrationTests.Projects;

namespace ProjectManager.IntegrationTests.Tasks;

/// <summary>Organização com um projeto e o dono dele (Gerente da equipe).</summary>
internal sealed record Workspace(TestUser Owner, Guid OrganizationId, Guid ProjectId);

/// <summary>Operações de planejamento e tarefas pela API, sempre com o cabeçalho de organização.</summary>
internal sealed class TaskTestKit(HttpClient client, MailpitFixture mailpit)
{
    public ProjectTestKit Projects { get; } = new(client, mailpit);

    public OrganizationTestKit Organizations => Projects.Organizations;

    public async Task<Workspace> CreateWorkspaceAsync(string projectName = "Projeto com tarefas")
    {
        var owner = await Organizations.CreateUserAsync();
        var organization = await Organizations.CreateOrganizationAsync(owner);
        var project = await Projects.CreateProjectAsync(owner, organization.Id, new { name = projectName });

        return new Workspace(owner, organization.Id, project.Id);
    }

    /// <summary>Cria um usuário, coloca-o na organização (Membro) e na equipe do projeto com o papel informado.</summary>
    public async Task<TestUser> AddTeamMemberAsync(Workspace workspace, string projectRole)
    {
        var user = await Organizations.CreateUserAsync();
        await Organizations.AddMemberAsync(workspace.Owner, workspace.OrganizationId, user, "Member");
        await Projects.AddToTeamAsync(workspace.Owner, workspace.OrganizationId, workspace.ProjectId, user, projectRole);

        return user;
    }

    public Task<HttpResponseMessage> SendToProjectAsync(HttpMethod method, Workspace workspace, string subPath, TestUser user, object? body = null) =>
        Projects.SendToProjectAsync(method, workspace.ProjectId, subPath, user, workspace.OrganizationId, body);

    public Task<HttpResponseMessage> SendToTaskAsync(HttpMethod method, Guid taskId, string subPath, TestUser user, Guid organizationId, object? body = null) =>
        Projects.SendAsync(method, $"/api/v1/tasks/{taskId}{subPath}", user, body, organizationId.ToString());

    public Task<HttpResponseMessage> SendToTaskAsync(HttpMethod method, Workspace workspace, Guid taskId, string subPath, TestUser user, object? body = null) =>
        SendToTaskAsync(method, taskId, subPath, user, workspace.OrganizationId, body);

    public async Task<TaskDetails> CreateTaskAsync(Workspace workspace, TestUser user, object body)
    {
        using var response = await SendToProjectAsync(HttpMethod.Post, workspace, "/tasks", user, body);
        response.EnsureSuccessStatusCode();

        return await ReadAsync<TaskDetails>(response);
    }

    public Task<TaskDetails> CreateTaskAsync(Workspace workspace, string title) =>
        CreateTaskAsync(workspace, workspace.Owner, new { title });

    public async Task<T> CreateInProjectAsync<T>(Workspace workspace, string subPath, object body)
    {
        using var response = await SendToProjectAsync(HttpMethod.Post, workspace, subPath, workspace.Owner, body);
        response.EnsureSuccessStatusCode();

        return await ReadAsync<T>(response);
    }

    public async Task<ProjectDetails> CreateSecondProjectAsync(Workspace workspace, string name = "Outro projeto") =>
        await Projects.CreateProjectAsync(workspace.Owner, workspace.OrganizationId, new { name });

    public static Task<T> ReadAsync<T>(HttpResponseMessage response) => ProjectTestKit.ReadAsync<T>(response);
}
