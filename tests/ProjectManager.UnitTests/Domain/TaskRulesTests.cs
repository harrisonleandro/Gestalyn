using ProjectManager.Domain.Common;
using ProjectManager.Domain.Projects;
using ProjectManager.Domain.Tasks;

namespace ProjectManager.UnitTests.Domain;

/// <summary>Regras da Fase 06: número, fases, marcos, tarefas, responsáveis, comentários e dependências.</summary>
public sealed class TaskRulesTests
{
    private static readonly Guid OrganizationId = Guid.CreateVersion7();
    private static readonly Guid UserId = Guid.CreateVersion7();
    private static readonly DateTimeOffset Now = new(2027, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void TaskNumbers_AreSequentialPerProject_AndNeverReused()
    {
        var project = new Project(OrganizationId, "Projeto");

        var numbers = Enumerable.Range(0, 3).Select(_ => project.AllocateTaskNumber()).ToList();

        Assert.Equal([1, 2, 3], numbers);
        Assert.Equal(4, project.NextTaskNumber);
    }

    [Fact]
    public void ArchivedProject_DoesNotAllocateNumbersNorAcceptTasksOrMilestones()
    {
        var project = new Project(OrganizationId, "Projeto");
        project.Archive(UserId, Now);

        Assert.Throws<InvalidOperationException>(() => project.AllocateTaskNumber());
        Assert.Throws<InvalidOperationException>(() => new ProjectTask(project, 1, "Tarefa", UserId));
        Assert.Throws<InvalidOperationException>(() => new Milestone(project, "Marco"));
    }

    [Fact]
    public void Phase_RejectsEndBeforeStart_AndProjectLimitsThePhases()
    {
        var project = new Project(OrganizationId, "Projeto");
        var phase = project.AddPhase("Fase");
        var day = new DateOnly(2027, 3, 1);

        phase.Schedule(day, day);
        Assert.Throws<ArgumentException>(() => phase.Schedule(day, day.AddDays(-1)));
        Enumerable.Range(2, ProjectPhase.MaxPerProject - 1).ToList().ForEach(i => project.AddPhase($"Fase {i}"));

        Assert.Throws<InvalidOperationException>(() => project.AddPhase("Excedente"));
        Assert.Equal(ProjectPhase.MaxPerProject, project.Phases.Count);
    }

    [Fact]
    public void Milestone_AcceptsOnlyPhasesOfItsProject_AndKeepsTheFirstReachedDate()
    {
        var project = new Project(OrganizationId, "Projeto");
        var otherProject = new Project(OrganizationId, "Outro");
        var milestone = new Milestone(project, "Marco");

        milestone.LinkToPhase(project.AddPhase("Fase"));
        milestone.MarkAsReached(Now);
        milestone.MarkAsReached(Now.AddDays(1));

        Assert.Throws<ArgumentException>(() => milestone.LinkToPhase(otherProject.AddPhase("Alheia")));
        Assert.Equal(Now, milestone.ReachedAt);
        milestone.MarkAsNotReached();
        Assert.Null(milestone.ReachedAt);
    }

    [Fact]
    public void Task_LinksOnlyToPhasesAndMilestonesOfItsProject()
    {
        var project = new Project(OrganizationId, "Projeto");
        var otherProject = new Project(OrganizationId, "Outro");
        var task = new ProjectTask(project, 1, "Tarefa", UserId);

        task.MoveToPhase(project.AddPhase("Fase"));
        task.LinkToMilestone(new Milestone(project, "Marco"));

        Assert.NotNull(task.PhaseId);
        Assert.NotNull(task.MilestoneId);
        Assert.Throws<ArgumentException>(() => task.MoveToPhase(otherProject.AddPhase("Alheia")));
        Assert.Throws<ArgumentException>(() => task.LinkToMilestone(new Milestone(otherProject, "Alheio")));
        Assert.Equal(Priority.Medium, task.Priority);
    }

    [Fact]
    public void Assign_IgnoresRepeatedUsers_AndLimitsTheAssignees()
    {
        var task = new ProjectTask(new Project(OrganizationId, "Projeto"), 1, "Tarefa", UserId);

        Assert.True(task.Assign(UserId, Now));
        Assert.False(task.Assign(UserId, Now));
        Enumerable.Range(1, ProjectTask.MaxAssignees - 1).ToList().ForEach(_ => task.Assign(Guid.CreateVersion7(), Now));

        Assert.Throws<InvalidOperationException>(() => task.Assign(Guid.CreateVersion7(), Now));
        Assert.True(task.Unassign(UserId));
        Assert.False(task.Unassign(UserId));
        Assert.Equal(ProjectTask.MaxAssignees - 1, task.Assignments.Count);
    }

    [Fact]
    public void ArchivedTask_IsReadOnly_AndArchivingAgainKeepsTheFirstRecord()
    {
        var task = new ProjectTask(new Project(OrganizationId, "Projeto"), 1, "Tarefa", UserId);

        task.Archive(UserId, Now);
        task.Archive(Guid.CreateVersion7(), Now.AddDays(1));

        Assert.Equal((Now, UserId), (task.ArchivedAt, task.ArchivedByUserId));
        Assert.Throws<InvalidOperationException>(() => task.Rename("Outro"));
        Assert.Throws<InvalidOperationException>(() => task.SetLabels(["x"]));
        Assert.Throws<InvalidOperationException>(() => task.Assign(Guid.CreateVersion7(), Now));
        Assert.Throws<InvalidOperationException>(() => new TaskComment(task, UserId, "Comentário"));
    }

    [Fact]
    public void TaskLabels_FollowTheProjectTagRules()
    {
        var task = new ProjectTask(new Project(OrganizationId, "Projeto"), 1, "Tarefa", UserId);

        task.SetLabels([" Bug ", "BUG", "ui"]);

        Assert.Equal(["Bug", "ui"], task.Labels.Select(label => label.Name));
        Assert.Throws<ArgumentException>(() => task.SetLabels([new string('x', Labels.NameMaxLength + 1)]));
        Assert.Throws<ArgumentException>(() => task.SetLabels(Enumerable.Range(0, Labels.MaxPerItem + 1).Select(i => $"l{i}")));
    }

    [Fact]
    public void Comment_MarksEditsOnlyWhenTheTextChanges()
    {
        var task = new ProjectTask(new Project(OrganizationId, "Projeto"), 1, "Tarefa", UserId);
        var comment = new TaskComment(task, UserId, " Texto ");

        comment.Edit("Texto", Now);
        Assert.Null(comment.EditedAt);

        comment.Edit("Texto novo", Now);
        Assert.Equal(("Texto novo", (DateTimeOffset?)Now), (comment.Body, comment.EditedAt));
        Assert.True(comment.IsAuthor(UserId));
        Assert.Throws<ArgumentException>(() => comment.Edit("   ", Now));
    }

    [Fact]
    public void Dependency_RequiresDifferentTasksOfTheSameProject()
    {
        var project = new Project(OrganizationId, "Projeto");
        var task = new ProjectTask(project, 1, "A", UserId);
        var other = new ProjectTask(project, 2, "B", UserId);
        var foreign = new ProjectTask(new Project(OrganizationId, "Outro"), 1, "C", UserId);

        var dependency = new TaskDependency(task, other, UserId, Now);

        Assert.Equal((task.Id, other.Id, project.Id), (dependency.TaskId, dependency.DependsOnTaskId, dependency.ProjectId));
        Assert.Throws<ArgumentException>(() => new TaskDependency(task, task, UserId, Now));
        Assert.Throws<ArgumentException>(() => new TaskDependency(task, foreign, UserId, Now));
    }

    [Fact]
    public void CycleDetection_FindsDirectAndLongCycles_AndAcceptsDiamonds()
    {
        var ids = Enumerable.Range(0, 6).Select(_ => Guid.CreateVersion7()).ToArray();
        (Guid, Guid)[] chain = [(ids[0], ids[1]), (ids[1], ids[2]), (ids[2], ids[3]), (ids[3], ids[4])];
        (Guid, Guid)[] diamond = [(ids[0], ids[1]), (ids[0], ids[2]), (ids[1], ids[3]), (ids[2], ids[3])];

        Assert.True(TaskDependency.WouldCreateCycle(chain, ids[1], ids[0]));
        Assert.True(TaskDependency.WouldCreateCycle(chain, ids[4], ids[0]));
        Assert.True(TaskDependency.WouldCreateCycle(chain, ids[2], ids[2]));
        Assert.False(TaskDependency.WouldCreateCycle(chain, ids[0], ids[4]));
        Assert.False(TaskDependency.WouldCreateCycle(chain, ids[5], ids[0]));
        Assert.False(TaskDependency.WouldCreateCycle(diamond, ids[0], ids[3]));
        Assert.True(TaskDependency.WouldCreateCycle(diamond, ids[3], ids[0]));
    }

    [Fact]
    public void ProjectRoles_GrantTaskPermissions()
    {
        Assert.Contains(ProjectPermissions.Plan, ProjectPermissions.For(ProjectRole.Manager));
        Assert.Contains(ProjectPermissions.TasksArchive, ProjectPermissions.For(ProjectRole.Manager));
        Assert.Contains(ProjectPermissions.TasksWrite, ProjectPermissions.For(ProjectRole.Contributor));
        Assert.Contains(ProjectPermissions.TasksComment, ProjectPermissions.For(ProjectRole.Contributor));
        Assert.DoesNotContain(ProjectPermissions.TasksArchive, ProjectPermissions.For(ProjectRole.Contributor));
        Assert.DoesNotContain(ProjectPermissions.Plan, ProjectPermissions.For(ProjectRole.Contributor));
        Assert.Equal([ProjectPermissions.Read], ProjectPermissions.For(ProjectRole.Viewer));
    }
}
