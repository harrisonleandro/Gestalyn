using ProjectManager.Domain.Agents;

namespace ProjectManager.UnitTests.Domain;

/// <summary>Regras do relatório de trabalho (ADR-016, D8): limites da declaração, SHA do commit e retrato do recebimento.</summary>
public sealed class WorkReportRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 22, 9, 0, 0, TimeSpan.Zero);
    private static readonly WorkReportStateSnapshot InProgress = new(Guid.CreateVersion7(), "Em andamento");

    [Theory]
    [InlineData("0123456789abcdef0123456789abcdef01234567", true)]
    [InlineData("0123456789ABCDEF0123456789ABCDEF01234567", true)]
    [InlineData("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef", true)]
    [InlineData("0123456789abcdef0123456789abcdef0123456", false)]
    [InlineData("0123456789abcdef0123456789abcdef012345678", false)]
    [InlineData("g123456789abcdef0123456789abcdef01234567", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsCommitSha_AcceptsOnly40Or64HexDigits(string? sha, bool expected) =>
        Assert.Equal(expected, WorkReport.IsCommitSha(sha));

    [Fact]
    public void Report_KeepsTheDeclarationAndTheSnapshot_WithTheShaInLowerCase()
    {
        var suggested = new WorkReportStateSnapshot(Guid.CreateVersion7(), "Em revisão");
        var report = Create(
            new WorkReportDeclaration(
                "  Resumo  ",
                "## Alterações",
                ["src/App.cs"],
                new WorkReportTests("dotnet test", 3, 0, 1),
                new WorkReportCommit(new string('A', 40), "org/app")),
            taskState: InProgress,
            suggestedState: suggested);

        Assert.Equal("Resumo", report.Summary);
        Assert.Equal(["src/App.cs"], report.Files);
        Assert.Equal((3, 0, 1), (report.TestsPassed, report.TestsFailed, report.TestsSkipped));
        Assert.Equal(new string('a', 40), report.CommitSha);
        Assert.Equal((InProgress.StateId, "Em andamento"), (report.TaskStateId, report.TaskStateName));
        Assert.Equal((suggested.StateId, "Em revisão"), (report.SuggestedStateId, report.SuggestedStateName));
    }

    [Fact]
    public void Report_RefusesWhatTheApiWouldNotAccept()
    {
        Assert.ThrowsAny<ArgumentException>(() => Create(Declaration(summary: " ")));
        Assert.ThrowsAny<ArgumentException>(() => Create(Declaration(summary: new string('s', WorkReport.SummaryMaxLength + 1))));
        Assert.ThrowsAny<ArgumentException>(() => Create(Declaration(files: [.. Enumerable.Range(0, WorkReport.MaxFiles + 1).Select(i => $"f{i}")])));
        Assert.ThrowsAny<ArgumentException>(() => Create(Declaration(files: [new string('f', WorkReport.FilePathMaxLength + 1)])));
        Assert.ThrowsAny<ArgumentException>(() => Create(Declaration(tests: new WorkReportTests(null, -1, null, null))));
        Assert.ThrowsAny<ArgumentException>(() => Create(Declaration(commit: new WorkReportCommit("abc", null))));

        Assert.Equal(WorkReport.MaxFiles, Create(Declaration(files: [.. Enumerable.Range(0, WorkReport.MaxFiles).Select(i => $"f{i}")])).Files.Count);
    }

    [Fact]
    public void TaskState_GoesWithTheTask_AndOnlyWithIt()
    {
        Assert.Throws<ArgumentException>(() => Create(Declaration(), taskId: Guid.CreateVersion7(), taskState: null));
        Assert.Throws<ArgumentException>(() => Create(Declaration(), taskId: null, taskState: InProgress));
        Assert.Null(Create(Declaration(), taskId: null, taskState: null).TaskStateId);
    }

    private static WorkReportDeclaration Declaration(
        string summary = "Resumo",
        IReadOnlyList<string>? files = null,
        WorkReportTests? tests = null,
        WorkReportCommit? commit = null) => new(summary, null, files, tests, commit);

    private static WorkReport Create(
        WorkReportDeclaration declaration,
        WorkReportStateSnapshot? taskState = null,
        WorkReportStateSnapshot? suggestedState = null) =>
        Create(declaration, taskState is null ? null : Guid.CreateVersion7(), taskState, suggestedState);

    private static WorkReport Create(
        WorkReportDeclaration declaration,
        Guid? taskId,
        WorkReportStateSnapshot? taskState,
        WorkReportStateSnapshot? suggestedState = null) =>
        new(Guid.CreateVersion7(), Guid.CreateVersion7(), taskId, Guid.CreateVersion7(), Guid.CreateVersion7(),
            declaration, taskState, suggestedState, "req-1", Now);
}
