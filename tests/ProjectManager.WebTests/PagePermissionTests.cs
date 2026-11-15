using System.Net;
using ProjectManager.Web.Api;
using ProjectManager.Web.Components.Pages.Organizations;
using ProjectManager.Web.Components.Pages.Projects;
using ProjectManager.Web.Components.Pages.Tasks;
using ProjectManager.Web.Components.Shared;
using ProjectManager.WebTests.Infrastructure;

namespace ProjectManager.WebTests;

/// <summary>
/// Permissões refletidas na interface (ADR-015, D4): o que aparece depende só das permissões e transições que a API
/// devolve. A validação continua na API; aqui se garante que a tela não oferece o que seria recusado.
/// </summary>
public sealed class PagePermissionTests : IDisposable
{
    private static readonly Guid TaskId = Guid.Parse("01a1df32-0000-7000-8000-000000000010");
    private static readonly Guid Review = Guid.Parse("01a1df32-0000-7000-8000-000000000021");
    private static readonly Guid Done = Guid.Parse("01a1df32-0000-7000-8000-000000000022");

    private readonly WebTestContext _context = new();

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OrganizationMenu_ShowsActivitiesOnlyWithThePermission(bool canReadAll)
    {
        var membership = new MyMembership(WebTestContext.OrganizationId, "Riuk Labs", Guid.NewGuid(), "Membro", false,
            canReadAll ? [Permissions.ActivityReadAll] : []);

        var cut = _context.Render<OrgNav>(parameters => parameters.Add(p => p.Membership, membership));

        Assert.Equal(canReadAll, cut.Markup.Contains("Atividades", StringComparison.Ordinal));
        Assert.Contains("Projetos", cut.Markup, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void NewProjectButton_RequiresProjectsCreate(bool canCreate)
    {
        _context.Membership(canCreate ? [Permissions.ProjectsCreate] : []);
        _context.Api.Json(HttpMethod.Get, $"organizations/{WebTestContext.OrganizationId}/dashboard",
            new OrganizationDashboard(new DateOnly(2026, 11, 15), 0, 0, 0, 0, [], 0, []));

        var cut = _context.Render<OrganizationHome>(parameters => parameters.Add(p => p.OrganizationId, WebTestContext.OrganizationId));

        cut.WaitForAssertion(() => Assert.Contains("Projetos ativos", cut.Markup, StringComparison.Ordinal));
        Assert.Equal(canCreate, cut.Markup.Contains("Novo projeto", StringComparison.Ordinal));
    }

    [Fact]
    public void NonMembers_SeeNotFound_AndNoOrganizationData()
    {
        _context.Api.Problem(HttpMethod.Get, $"organizations/{WebTestContext.OrganizationId}/me", HttpStatusCode.NotFound, "Organização não encontrada.");

        var cut = _context.Render<OrganizationHome>(parameters => parameters.Add(p => p.OrganizationId, WebTestContext.OrganizationId));

        cut.WaitForAssertion(() => Assert.Contains("Não encontrado", cut.Markup, StringComparison.Ordinal));
        Assert.DoesNotContain(_context.Api.Calls, call => call.Contains("/dashboard", StringComparison.Ordinal));
    }

    [Fact]
    public void ProjectSettings_ForAViewer_OffersNoChanges()
    {
        _context.Membership(Permissions.MembersRead);
        _context.Project();
        StubTeam();

        var cut = _context.Render<ProjectSettings>(parameters => parameters
            .Add(p => p.OrganizationId, WebTestContext.OrganizationId)
            .Add(p => p.ProjectId, WebTestContext.ProjectId));

        cut.WaitForAssertion(() => Assert.Contains("Equipe (1)", cut.Markup, StringComparison.Ordinal));
        Assert.Empty(cut.FindAll("form[method=post]"));
        Assert.DoesNotContain("Arquivar projeto", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Adicionar pessoa", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ProjectSettings_ForAManager_OffersEditTeamRepositoriesAndArchive()
    {
        _context.Membership(Permissions.MembersRead);
        _context.Project(Permissions.ProjectUpdate, Permissions.ProjectArchive, Permissions.ProjectMembersManage);
        StubTeam();

        var cut = _context.Render<ProjectSettings>(parameters => parameters
            .Add(p => p.OrganizationId, WebTestContext.OrganizationId)
            .Add(p => p.ProjectId, WebTestContext.ProjectId));

        cut.WaitForAssertion(() => Assert.Contains("Equipe (1)", cut.Markup, StringComparison.Ordinal));
        Assert.NotNull(cut.Find("form[data-confirm*='Arquivar Portal']"));
        Assert.Contains("Adicionar pessoa da organização", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Associar", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Salvar", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void TaskDetails_ForAViewer_ShowsNoTransitionsCommentsOrEditing()
    {
        _context.Membership();
        _context.Project();
        StubTask(permissions: ["project.read"], transitions: []);

        var cut = RenderTask();

        cut.WaitForAssertion(() => Assert.Contains("#7 Tela de login", cut.Markup, StringComparison.Ordinal));
        Assert.Contains("Nenhuma transição disponível", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Novo comentário", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Editar tarefa", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Enviar arquivo", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void TaskDetails_OffersExactlyTheTransitionsTheApiReturns()
    {
        _context.Membership();
        _context.Project(Permissions.TasksWrite, Permissions.TasksComment);
        StubTask(
            permissions: ["project.read", Permissions.TasksWrite, Permissions.TasksComment],
            transitions: [new AvailableTransition(Guid.NewGuid(), Done, "Concluída", StateCategory.Done, RequiresApproval: true)]);

        var cut = RenderTask();

        cut.WaitForAssertion(() => Assert.Contains("#7 Tela de login", cut.Markup, StringComparison.Ordinal));
        var options = cut.FindAll("#transition-target option").Select(option => option.TextContent).ToList();
        Assert.Equal(["Concluída (aprovação)"], options);
        Assert.Contains("Novo comentário", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Editar tarefa", cut.Markup, StringComparison.Ordinal);
    }

    public void Dispose() => _context.Dispose();

    private IRenderedComponent<TaskDetailsPage> RenderTask() =>
        _context.Render<TaskDetailsPage>(parameters => parameters
            .Add(p => p.OrganizationId, WebTestContext.OrganizationId)
            .Add(p => p.TaskId, TaskId));

    private void StubTeam()
    {
        _context.Api.Json(HttpMethod.Get, $"projects/{WebTestContext.ProjectId}/members",
            new[] { new ProjectMemberDetails(WebTestContext.UserId, "Pessoa de Teste", "pessoa@teste", ProjectRole.Manager, true, DateTimeOffset.UnixEpoch) });
        _context.Api.Json(HttpMethod.Get, $"projects/{WebTestContext.ProjectId}/repositories", Array.Empty<ProjectRepositoryDetails>());
        _context.Api.Json(HttpMethod.Get, $"organizations/{WebTestContext.OrganizationId}/members",
            new[] { new MemberDetails(Guid.NewGuid(), "Outra Pessoa", "outra@teste", Guid.NewGuid(), "Membro", DateTimeOffset.UnixEpoch) });
    }

    private void StubTask(IReadOnlyList<string> permissions, IReadOnlyList<AvailableTransition> transitions)
    {
        var task = new TaskDetails(
            TaskId, WebTestContext.ProjectId, 7, "Tela de login", "Descrição", Priority.High, null, null, null, [], [], [], [],
            new TaskState(Review, "Em revisão", StateCategory.InProgress, false), transitions, WebTestContext.UserId,
            null, null, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, permissions);
        _context.Api.Json(HttpMethod.Get, $"tasks/{TaskId}", task);
        _context.Api.Json(HttpMethod.Get, $"tasks/{TaskId}/comments", Array.Empty<CommentDetails>());
        _context.Api.Json(HttpMethod.Get, $"tasks/{TaskId}/attachments", Array.Empty<AttachmentDetails>());
        _context.Api.Json(HttpMethod.Get, $"tasks/{TaskId}/history", Array.Empty<TaskHistoryEntry>());
        _context.Api.Json(HttpMethod.Get, $"projects/{WebTestContext.ProjectId}/phases", Array.Empty<PhaseDetails>());
        _context.Api.Json(HttpMethod.Get, $"projects/{WebTestContext.ProjectId}/milestones", Array.Empty<MilestoneDetails>());
        _context.Api.Json(HttpMethod.Get, $"projects/{WebTestContext.ProjectId}/members", Array.Empty<ProjectMemberDetails>());
        _context.Api.Json(HttpMethod.Get, $"projects/{WebTestContext.ProjectId}/tasks", new Paged<TaskSummary>([], 1, 100, 0));
    }
}
