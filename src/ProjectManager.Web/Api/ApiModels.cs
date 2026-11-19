using System.Text.Json;

namespace ProjectManager.Web.Api;

// Contratos da API vistos pela Web. A Web não referencia Domain nem Application (AGENTS.md): estes tipos espelham
// o JSON das respostas, com os mesmos nomes, e servem só para exibição.

public enum Priority { Low, Medium, High, Critical }

public enum ProjectRole { Manager, Contributor, Viewer }

public enum RepositoryProvider { GitHub, GitLab, Bitbucket, Other }

public enum StateCategory { Todo, InProgress, Blocked, Done, Canceled }

public enum ActorKind { User, Agent }

public enum ActivityOrigin { Api, Web, Cli, Mcp, System }

public enum ActivitySource { Event, Log }

public enum ActivityLogKind { Note, Meeting, Other }

public enum DecisionStatus { Proposed, Accepted, Rejected, Superseded }

public enum TaskSortField { Number, DueDate, Priority, UpdatedAt, State }

public enum SortDirection { Asc, Desc }

public sealed record Paged<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int PageCount => TotalCount == 0 ? 1 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}

public sealed record OrganizationSummary(Guid Id, string Name, Guid RoleId, string RoleName);

public sealed record OrganizationDetails(Guid Id, string Name, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public sealed record MyMembership(Guid OrganizationId, string Name, Guid RoleId, string RoleName, bool IsOwner, IReadOnlyList<string> Permissions)
{
    public bool Can(string permission) => Permissions.Contains(permission, StringComparer.Ordinal);
}

public sealed record MemberDetails(Guid UserId, string DisplayName, string Email, Guid RoleId, string RoleName, DateTimeOffset JoinedAt);

public sealed record RoleDetails(Guid Id, string Name, string? SystemRole, IReadOnlyList<string> Permissions);

public sealed record InvitationDetails(Guid Id, string Email, Guid RoleId, string RoleName, string Status, DateTimeOffset ExpiresAt, DateTimeOffset CreatedAt);

public sealed record ProjectSummary(
    Guid Id,
    string Name,
    Priority Priority,
    DateOnly? StartDate,
    DateOnly? DueDate,
    IReadOnlyList<string> Tags,
    DateTimeOffset? ArchivedAt,
    ProjectRole? MyRole);

public sealed record ProjectResponsible(Guid UserId, string DisplayName);

public sealed record ProjectDetails(
    Guid Id,
    string Name,
    string? Description,
    Priority Priority,
    DateOnly? StartDate,
    DateOnly? DueDate,
    IReadOnlyList<string> Tags,
    ProjectResponsible? Responsible,
    DateTimeOffset? ArchivedAt,
    Guid? ArchivedByUserId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    ProjectRole? MyRole,
    IReadOnlyList<string> MyPermissions)
{
    public bool Can(string permission) => MyPermissions.Contains(permission, StringComparer.Ordinal);
}

public sealed record ProjectMemberDetails(Guid UserId, string DisplayName, string Email, ProjectRole Role, bool IsResponsible, DateTimeOffset JoinedAt);

public sealed record ProjectRepositoryDetails(Guid Id, string Url, string? Name, RepositoryProvider Provider, DateTimeOffset CreatedAt);

public sealed record PhaseDetails(Guid Id, string Name, string? Description, int Position, DateOnly? StartDate, DateOnly? EndDate, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public sealed record MilestoneDetails(Guid Id, string Name, string? Description, DateOnly? DueDate, Guid? PhaseId, DateTimeOffset? ReachedAt, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public sealed record TaskState(Guid Id, string Name, StateCategory Category, bool IsFinal);

public sealed record AvailableTransition(Guid TransitionId, Guid ToStateId, string ToStateName, StateCategory ToCategory, bool RequiresApproval);

public sealed record TaskSummary(
    Guid Id,
    int Number,
    string Title,
    TaskState State,
    Priority Priority,
    DateOnly? DueDate,
    Guid? PhaseId,
    Guid? MilestoneId,
    IReadOnlyList<string> Labels,
    IReadOnlyList<Guid> AssigneeIds,
    DateTimeOffset? ArchivedAt,
    DateTimeOffset UpdatedAt);

public sealed record TaskAssignee(Guid UserId, string DisplayName, DateTimeOffset AssignedAt);

public sealed record TaskReference(Guid Id, int Number, string Title, DateTimeOffset? ArchivedAt);

/// <summary>Autor de algo: pessoa ou agente (ADR-016).</summary>
public sealed record ActorReference(Guid Id, ActorKind Kind, string? Name);

public sealed record TaskDetails(
    Guid Id,
    Guid ProjectId,
    int Number,
    string Title,
    string? Description,
    Priority Priority,
    DateOnly? DueDate,
    Guid? PhaseId,
    Guid? MilestoneId,
    IReadOnlyList<string> Labels,
    IReadOnlyList<TaskAssignee> Assignees,
    IReadOnlyList<TaskReference> DependsOn,
    IReadOnlyList<TaskReference> Blocks,
    TaskState State,
    IReadOnlyList<AvailableTransition> AvailableTransitions,
    Guid? CreatedByUserId,
    DateTimeOffset? ArchivedAt,
    Guid? ArchivedByUserId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<string> MyPermissions,
    ActorReference? CreatedBy = null,
    int Version = 0)
{
    public bool Can(string permission) => MyPermissions.Contains(permission, StringComparer.Ordinal);
}

public sealed record CommentDetails(Guid Id, Guid AuthorUserId, string AuthorDisplayName, string Body, DateTimeOffset CreatedAt, DateTimeOffset? EditedAt);

public sealed record TaskHistoryEntry(
    Guid Id,
    Guid? FromStateId,
    string? FromStateName,
    Guid ToStateId,
    string ToStateName,
    Guid ChangedById,
    string? ChangedByName,
    ActorKind ActorKind,
    string? Comment,
    DateTimeOffset ChangedAt);

public sealed record AttachmentDetails(Guid Id, string FileName, string ContentType, long SizeBytes, string Sha256, Guid UploadedByUserId, DateTimeOffset CreatedAt);

public sealed record WorkflowStateDetails(Guid Id, string Name, StateCategory Category, bool IsFinal, bool IsInitial, int Position, int TaskCount);

public sealed record WorkflowTransitionDetails(Guid Id, Guid FromStateId, Guid ToStateId, bool RequiresApproval);

public sealed record WorkflowDetails(Guid ProjectId, bool RequireIndependentApproval, IReadOnlyList<WorkflowStateDetails> States, IReadOnlyList<WorkflowTransitionDetails> Transitions);

public sealed record ActivityItem(
    Guid Id,
    ActivitySource Source,
    Guid? ProjectId,
    Guid? TaskId,
    Guid? ActorId,
    string? ActorName,
    ActorKind ActorKind,
    ActivityOrigin Origin,
    string? RequestId,
    string? Action,
    string? EntityType,
    Guid? EntityId,
    ActivityLogKind? Kind,
    string Summary,
    string? Details,
    JsonElement? Changes,
    DateTimeOffset OccurredAt,
    DateTimeOffset? RecordedAt);

public sealed record CategoryCounts(int Todo, int InProgress, int Blocked, int Done, int Canceled, int Total);

public sealed record Progress(CategoryCounts Counts, double? Percent, string Rule);

public sealed record TaskBrief(Guid Id, Guid ProjectId, int Number, string Title, DateOnly? DueDate, string StateName, StateCategory Category);

public sealed record PhaseProgress(Guid PhaseId, string Name, int Position, Progress Progress);

public sealed record MilestoneProgress(Guid MilestoneId, string Name, DateOnly? DueDate, DateTimeOffset? ReachedAt, Progress Progress);

public sealed record ProjectProgress(Guid ProjectId, Progress Project, IReadOnlyList<PhaseProgress> Phases, IReadOnlyList<MilestoneProgress> Milestones);

public sealed record StateCount(Guid StateId, string Name, StateCategory Category, int Count);

public sealed record ProjectDashboard(
    Guid ProjectId,
    string Name,
    DateOnly Today,
    Progress Progress,
    IReadOnlyList<StateCount> ByState,
    int OverdueCount,
    IReadOnlyList<TaskBrief> Overdue,
    int DueSoonCount,
    IReadOnlyList<TaskBrief> DueSoon,
    int BlockedCount,
    IReadOnlyList<TaskBrief> Blocked,
    IReadOnlyList<MilestoneProgress> UpcomingMilestones,
    IReadOnlyList<ActivityItem> RecentActivity);

public sealed record WeeklyThroughput(DateOnly WeekStart, int Created, int Completed);

public sealed record CycleTime(int SampleSize, double? MedianDays, double? Percentile85Days);

public sealed record ProjectStatistics(Guid ProjectId, int Weeks, DateOnly From, DateOnly To, IReadOnlyList<WeeklyThroughput> Weekly, CycleTime CycleTime);

public sealed record ProjectOverview(Guid ProjectId, string Name, Progress Progress, int OpenCount, int OverdueCount);

public sealed record OrganizationDashboard(
    DateOnly Today,
    int ProjectCount,
    int OpenCount,
    int OverdueCount,
    int CompletedLast7Days,
    IReadOnlyList<ProjectOverview> Projects,
    int MyOpenCount,
    IReadOnlyList<TaskBrief> MyOpenTasks);

public sealed record DocumentSummary(Guid Id, string Title, int CurrentVersion, Guid CreatedById, DateTimeOffset? ArchivedAt, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public sealed record DocumentDetails(
    Guid Id,
    Guid ProjectId,
    string Title,
    string Body,
    int CurrentVersion,
    Guid CreatedById,
    DateTimeOffset? ArchivedAt,
    Guid? ArchivedById,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record DocumentVersionSummary(int Number, string Title, Guid AuthorId, string? AuthorName, DateTimeOffset CreatedAt);

public sealed record DocumentVersionDetails(int Number, string Title, string Body, Guid AuthorId, string? AuthorName, DateTimeOffset CreatedAt);

public sealed record DecisionSummary(
    Guid Id,
    string Title,
    DecisionStatus Status,
    Guid? TaskId,
    Guid ProposedById,
    DateTimeOffset? DecidedAt,
    Guid? SupersededById,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record DecisionDetails(
    Guid Id,
    Guid ProjectId,
    string Title,
    string Context,
    string Outcome,
    string? Alternatives,
    string? Consequences,
    Guid? TaskId,
    DecisionStatus Status,
    Guid ProposedById,
    Guid? DecidedById,
    DateTimeOffset? DecidedAt,
    Guid? SupersededById,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>Códigos de permissão usados pela interface para decidir o que mostrar. A validação é sempre da API.</summary>
public static class Permissions
{
    public const string OrganizationUpdate = "organization.update";
    public const string MembersRead = "members.read";
    public const string MembersInvite = "members.invite";
    public const string MembersUpdateRole = "members.update_role";
    public const string MembersRemove = "members.remove";
    public const string ProjectsCreate = "projects.create";
    public const string ActivityReadAll = "activity.read_all";

    public const string ProjectUpdate = "project.update";
    public const string ProjectArchive = "project.archive";
    public const string ProjectMembersManage = "project.members.manage";
    public const string ProjectPlan = "project.plan";
    public const string TasksWrite = "tasks.write";
    public const string TasksComment = "tasks.comment";
    public const string TasksArchive = "tasks.archive";
    public const string TasksApprove = "tasks.approve";
    public const string WorkflowManage = "workflow.manage";
    public const string DocumentsWrite = "documents.write";
    public const string DocumentsManage = "documents.manage";
}

public static class RoleDetailsExtensions
{
    public static bool IsOwnerRole(this RoleDetails role) => role.SystemRole == "Owner";
}
