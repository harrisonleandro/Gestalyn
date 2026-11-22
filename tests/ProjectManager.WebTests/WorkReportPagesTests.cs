using ProjectManager.Web.Api;
using ProjectManager.Web.Components.Pages.Reports;
using ProjectManager.Web.Components.Pages.Tasks;
using ProjectManager.Web.Components.Shared;
using ProjectManager.WebTests.Infrastructure;

namespace ProjectManager.WebTests;

/// <summary>
/// Relatórios de agentes e autoria na Web (ADR-016, D8 e D13): os cinco níveis aparecem separados e rotulados, a
/// aprovação humana mostra quem aprovou, e o que um agente fez aparece como "Agente: nome".
/// </summary>
public sealed class WorkReportPagesTests : IDisposable
{
    private static readonly Guid TaskId = Guid.Parse("01a1df32-0000-7000-8000-000000000201");
    private static readonly Guid ReportId = Guid.Parse("01a1df32-0000-7000-8000-000000000202");
    private static readonly Guid AgentId = Guid.Parse("01a1df32-0000-7000-8000-000000000203");
    private static readonly ActorReference Agent = new(AgentId, ActorKind.Agent, "Robô");

    private readonly WebTestContext _context = new();

    [Fact]
    public void Evidence_PendingApproval_ShowsTheFiveLevelsSeparately()
    {
        var cut = _context.Render<WorkReportEvidenceList>(parameters => parameters
            .Add(p => p.Evidence, Evidence(WorkReportLevels.Pending)));

        Assert.Equal(
            ["Declarado pelo agente", "Recebido e validado pela API", "Testes não verificados", "CI não verificado", "Aprovação humana pendente"],
            cut.FindAll("li").Select(item => item.TextContent).ToList());
    }

    [Fact]
    public void Evidence_Approved_NamesWhoApprovedAndWhen()
    {
        var cut = _context.Render<WorkReportEvidenceList>(parameters => parameters
            .Add(p => p.Evidence, Evidence(WorkReportLevels.Approved))
            .Add(p => p.Approval, new WorkReportApproval(WebTestContext.UserId, "Ana", new DateTimeOffset(2026, 11, 20, 14, 30, 0, TimeSpan.Zero))));

        Assert.Contains("Aprovado por Ana em 20/11/2026 14:30", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("pendente", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Evidence_WithoutTask_SaysApprovalDoesNotApply()
    {
        var cut = _context.Render<WorkReportEvidenceList>(parameters => parameters
            .Add(p => p.Evidence, Evidence(WorkReportLevels.NotApplicable)));

        Assert.Contains("não se aplica", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ReportPage_LabelsEverythingAsDeclared_AndShowsTheAgentAsAuthor()
    {
        _context.Membership();
        _context.Project();
        _context.Api.Json(HttpMethod.Get, $"reports/{ReportId}", new WorkReportDetails(
            ReportId, WebTestContext.ProjectId, TaskId, Agent, "Tela de login pronta", "- **Formulário** novo", ["src/Login.razor"],
            new WorkReportTests("dotnet test", 12, 0, 1), new WorkReportCommit(new string('a', 40), "Gestalyn"),
            new WorkReportStateSnapshot(Guid.NewGuid(), "Em andamento"), new WorkReportStateSnapshot(Guid.NewGuid(), "Em revisão"),
            "req-1", DateTimeOffset.UnixEpoch, Evidence(WorkReportLevels.Pending), null));

        var cut = _context.Render<WorkReportPage>(parameters => parameters
            .Add(p => p.OrganizationId, WebTestContext.OrganizationId)
            .Add(p => p.ReportId, ReportId));

        cut.WaitForAssertion(() => Assert.Contains("Tela de login pronta", cut.Markup, StringComparison.Ordinal));
        Assert.Contains("Agente: Robô", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Resumo (declarado pelo agente)", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Testes (declarados)", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("a API não executou os testes nem confirmou o CI", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Aprovação humana pendente", cut.Markup, StringComparison.Ordinal);

        // O Markdown das alterações passa pelo MarkdownView, que sanitiza.
        Assert.NotNull(cut.Find("strong"));
        Assert.Contains($"tarefas/{TaskId}", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ProjectTabs_IncludeTheReports()
    {
        _context.Membership();
        _context.Project();
        _context.Api.Json(HttpMethod.Get, $"projects/{WebTestContext.ProjectId}/reports", new Paged<WorkReportSummary>(
            [new WorkReportSummary(ReportId, WebTestContext.ProjectId, null, Agent, "Relatório sem tarefa", null, DateTimeOffset.UnixEpoch, Evidence(WorkReportLevels.NotApplicable))],
            1, 25, 1));

        var cut = _context.Render<ProjectReports>(parameters => parameters
            .Add(p => p.OrganizationId, WebTestContext.OrganizationId)
            .Add(p => p.ProjectId, WebTestContext.ProjectId));

        cut.WaitForAssertion(() => Assert.Contains("Relatório sem tarefa", cut.Markup, StringComparison.Ordinal));
        Assert.NotNull(cut.Find("nav.pm-tabs a[href$='/relatorios']"));
        Assert.NotNull(cut.Find($"a[href$='/relatorios/{ReportId}']"));
        Assert.Contains("Agente: Robô", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void TaskDetails_ShowsAgentAuthorship_HistoryAndReports()
    {
        _context.Membership();
        _context.Project();
        StubTask();

        var cut = _context.Render<TaskDetailsPage>(parameters => parameters
            .Add(p => p.OrganizationId, WebTestContext.OrganizationId)
            .Add(p => p.TaskId, TaskId));

        cut.WaitForAssertion(() => Assert.Contains("#9 Criada por agente", cut.Markup, StringComparison.Ordinal));
        Assert.Contains("Criada por", cut.Markup, StringComparison.Ordinal);
        // Autoria da tarefa, histórico e relatório.
        Assert.Equal(3, CountOf(cut.Markup, "Agente: Robô"));
        Assert.Contains("Relatórios de agentes", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Implementação concluída", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Aprovação humana pendente", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ActivityList_NamesAgentsAsAgents()
    {
        var items = new[]
        {
            Activity(AgentId, "Robô", ActorKind.Agent),
            Activity(AgentId, null, ActorKind.Agent),
            Activity(WebTestContext.UserId, "Ana", ActorKind.User),
            Activity(null, null, ActorKind.User),
        };

        var cut = _context.Render<ActivityList>(parameters => parameters.Add(p => p.Items, items));

        Assert.Contains("Agente: Robô", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Agente: removido", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Ana", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Sistema", cut.Markup, StringComparison.Ordinal);
    }

    public void Dispose() => _context.Dispose();

    private static WorkReportEvidence Evidence(string approval) =>
        new(true, true, WorkReportLevels.NotVerified, WorkReportLevels.NotVerified, approval);

    private static int CountOf(string text, string value)
    {
        var count = 0;
        for (var index = text.IndexOf(value, StringComparison.Ordinal); index >= 0; index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    private static ActivityItem Activity(Guid? actorId, string? name, ActorKind kind) => new(
        Guid.NewGuid(), ActivitySource.Event, WebTestContext.ProjectId, null, actorId, name, kind, ActivityOrigin.Api, null,
        "task.created", "task", null, null, "Tarefa criada", null, null, DateTimeOffset.UnixEpoch, null);

    private void StubTask()
    {
        var task = new TaskDetails(
            TaskId, WebTestContext.ProjectId, 9, "Criada por agente", null, Priority.Medium, null, null, null, [], [], [], [],
            new TaskState(Guid.NewGuid(), "Em andamento", StateCategory.InProgress, false), [], null,
            null, null, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, ["project.read"], Agent, 3);
        _context.Api.Json(HttpMethod.Get, $"tasks/{TaskId}", task);
        _context.Api.Json(HttpMethod.Get, $"tasks/{TaskId}/comments", Array.Empty<CommentDetails>());
        _context.Api.Json(HttpMethod.Get, $"tasks/{TaskId}/attachments", Array.Empty<AttachmentDetails>());
        _context.Api.Json(HttpMethod.Get, $"tasks/{TaskId}/history", new[]
        {
            new TaskHistoryEntry(Guid.NewGuid(), null, null, Guid.NewGuid(), "A fazer", AgentId, "Robô", ActorKind.Agent, null, DateTimeOffset.UnixEpoch),
        });
        _context.Api.Json(HttpMethod.Get, $"tasks/{TaskId}/reports", new Paged<WorkReportSummary>(
            [new WorkReportSummary(ReportId, WebTestContext.ProjectId, TaskId, Agent, "Implementação concluída", null, DateTimeOffset.UnixEpoch, Evidence(WorkReportLevels.Pending))],
            1, 50, 1));
        _context.Api.Json(HttpMethod.Get, $"projects/{WebTestContext.ProjectId}/phases", Array.Empty<PhaseDetails>());
        _context.Api.Json(HttpMethod.Get, $"projects/{WebTestContext.ProjectId}/milestones", Array.Empty<MilestoneDetails>());
    }
}
