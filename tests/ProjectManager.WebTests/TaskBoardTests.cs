using System.Net;
using ProjectManager.Web.Api;
using ProjectManager.Web.Components.Pages.Tasks;
using ProjectManager.WebTests.Infrastructure;

namespace ProjectManager.WebTests;

/// <summary>Quadro de tarefas (ADR-015, D5): uma coluna por estado, "Mover para…" pelas transições, erros da API exibidos.</summary>
public sealed class TaskBoardTests : IDisposable
{
    private static readonly Guid Todo = Guid.Parse("01a1df32-0000-7000-8000-000000000030");
    private static readonly Guid Review = Guid.Parse("01a1df32-0000-7000-8000-000000000031");
    private static readonly Guid Done = Guid.Parse("01a1df32-0000-7000-8000-000000000032");
    private static readonly Guid TaskId = Guid.Parse("01a1df32-0000-7000-8000-000000000033");

    private readonly WebTestContext _context = new();

    [Fact]
    public void Columns_FollowTheWorkflowOrder_WithTheTaskCount()
    {
        Arrange(Permissions.TasksWrite);

        var cut = RenderBoard();

        cut.WaitForAssertion(() => Assert.Equal(3, cut.FindAll(".pm-column").Count));
        Assert.Equal(["A fazer", "Em revisão", "Concluída"], cut.FindAll(".pm-column h2").Select(h => h.TextContent.Trim()));
        Assert.Contains("#4 Revisar contrato", cut.Find(".pm-column:nth-child(2)").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void WithoutTasksWrite_TheBoardOffersNoMoves()
    {
        Arrange();

        var cut = RenderBoard();

        cut.WaitForAssertion(() => Assert.Equal(3, cut.FindAll(".pm-column").Count));
        Assert.DoesNotContain("Mover para", cut.Find(".pm-board").TextContent, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ApprovalTransitions_AppearOnlyForApprovers(bool canApprove)
    {
        Arrange(canApprove ? [Permissions.TasksWrite, Permissions.TasksApprove] : [Permissions.TasksWrite]);

        var cut = RenderBoard();

        cut.WaitForAssertion(() => Assert.Equal(3, cut.FindAll(".pm-column").Count));
        var moves = cut.FindAll(".pm-column:nth-child(2) form button").Select(b => b.TextContent.Trim()).ToList();
        Assert.Contains("A fazer", moves);
        Assert.Equal(canApprove, moves.Contains("Concluída (exige aprovação)"));
    }

    [Fact]
    public void ARejectedMove_ShowsTheApiMessage_AndKeepsTheBoard()
    {
        Arrange(Permissions.TasksWrite);
        _context.Api.Problem(HttpMethod.Post, $"tasks/{TaskId}/transitions", HttpStatusCode.Conflict, "A tarefa mudou de estado desde que você a abriu.");

        var cut = RenderBoard();
        cut.WaitForAssertion(() => Assert.Equal(3, cut.FindAll(".pm-column").Count));
        cut.Find(".pm-column:nth-child(2) form").Submit();

        cut.WaitForAssertion(() => Assert.Contains("A tarefa mudou de estado desde que você a abriu.", cut.Find("[role=alert]").TextContent, StringComparison.Ordinal));
        Assert.Contains($"POST tasks/{TaskId}/transitions", _context.Api.Calls);
        Assert.Equal(3, cut.FindAll(".pm-column").Count);
    }

    public void Dispose() => _context.Dispose();

    private IRenderedComponent<TaskBoard> RenderBoard() =>
        _context.Render<TaskBoard>(parameters => parameters
            .Add(p => p.OrganizationId, WebTestContext.OrganizationId)
            .Add(p => p.ProjectId, WebTestContext.ProjectId));

    private void Arrange(params string[] permissions)
    {
        _context.Membership();
        _context.Project(permissions);
        _context.Api.Json(HttpMethod.Get, $"projects/{WebTestContext.ProjectId}/workflow", new WorkflowDetails(
            WebTestContext.ProjectId,
            false,
            [
                new WorkflowStateDetails(Todo, "A fazer", StateCategory.Todo, false, true, 1, 0),
                new WorkflowStateDetails(Review, "Em revisão", StateCategory.InProgress, false, false, 2, 1),
                new WorkflowStateDetails(Done, "Concluída", StateCategory.Done, true, false, 3, 0),
            ],
            [
                new WorkflowTransitionDetails(Guid.NewGuid(), Todo, Review, false),
                new WorkflowTransitionDetails(Guid.NewGuid(), Review, Todo, false),
                new WorkflowTransitionDetails(Guid.NewGuid(), Review, Done, true),
            ]));

        // A lista de tarefas é a mesma rota para todas as colunas: o teste devolve a tarefa em revisão em todas, e a coluna
        // mostra o que a API devolveu para o filtro do estado.
        _context.Api.Json(HttpMethod.Get, $"projects/{WebTestContext.ProjectId}/tasks", new Paged<TaskSummary>(
            [new TaskSummary(TaskId, 4, "Revisar contrato", new TaskState(Review, "Em revisão", StateCategory.InProgress, false),
                Priority.High, null, null, null, [], [], null, DateTimeOffset.UnixEpoch)],
            1, 50, 1));
    }
}
