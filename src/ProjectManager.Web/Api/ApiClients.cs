using System.Globalization;
using System.Net.Http.Headers;

namespace ProjectManager.Web.Api;

/// <summary>Monta a query string só com os filtros preenchidos.</summary>
internal static class Query
{
    public static string Of(params (string Name, object? Value)[] parameters)
    {
        var pairs = parameters
            .Where(parameter => parameter.Value is not null && parameter.Value is not "" && parameter.Value is not false)
            .Select(parameter => new KeyValuePair<string, string?>(parameter.Name, Format(parameter.Value!)));
        return QueryString.Create(pairs).ToUriComponent();
    }

    private static string Format(object value) => value switch
    {
        bool boolean => boolean ? "true" : "false",
        DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        DateTimeOffset moment => moment.ToString("O", CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString()!,
    };
}

/// <summary>Organizações, membros, convites e atividades da organização.</summary>
public sealed class OrganizationsApi(GestalynApi api)
{
    public Task<ApiResult<List<OrganizationSummary>>> ListMineAsync() => api.GetAsync<List<OrganizationSummary>>("organizations");

    public Task<ApiResult<OrganizationSummary>> CreateAsync(string name) =>
        api.SendAsync<OrganizationSummary>(HttpMethod.Post, "organizations", new { name });

    public Task<ApiResult<MyMembership>> GetMineAsync(Guid organizationId) => api.GetAsync<MyMembership>($"organizations/{organizationId}/me");

    public Task<ApiResult<OrganizationDetails>> RenameAsync(Guid organizationId, string name) =>
        api.SendAsync<OrganizationDetails>(HttpMethod.Patch, $"organizations/{organizationId}", new { name });

    public Task<ApiResult<List<MemberDetails>>> ListMembersAsync(Guid organizationId) => api.GetAsync<List<MemberDetails>>($"organizations/{organizationId}/members");

    public Task<ApiResult<List<RoleDetails>>> ListRolesAsync(Guid organizationId) => api.GetAsync<List<RoleDetails>>($"organizations/{organizationId}/roles");

    public Task<ApiResult> ChangeRoleAsync(Guid organizationId, Guid userId, Guid roleId) =>
        api.SendAsync(HttpMethod.Patch, $"organizations/{organizationId}/members/{userId}", new { roleId });

    public Task<ApiResult> RemoveMemberAsync(Guid organizationId, Guid userId) =>
        api.SendAsync(HttpMethod.Delete, $"organizations/{organizationId}/members/{userId}");

    public Task<ApiResult<List<InvitationDetails>>> ListInvitationsAsync(Guid organizationId) =>
        api.GetAsync<List<InvitationDetails>>($"organizations/{organizationId}/invitations");

    public Task<ApiResult> InviteAsync(Guid organizationId, string email, Guid roleId) =>
        api.SendAsync(HttpMethod.Post, $"organizations/{organizationId}/invitations", new { email, roleId });

    public Task<ApiResult> RevokeInvitationAsync(Guid organizationId, Guid invitationId) =>
        api.SendAsync(HttpMethod.Delete, $"organizations/{organizationId}/invitations/{invitationId}");

    public Task<ApiResult<OrganizationDashboard>> DashboardAsync(Guid organizationId) =>
        api.GetAsync<OrganizationDashboard>($"organizations/{organizationId}/dashboard");

    public Task<ApiResult<Paged<ActivityItem>>> ActivitiesAsync(Guid organizationId, ActivityFilter filter) =>
        api.GetAsync<Paged<ActivityItem>>($"organizations/{organizationId}/activities{filter.ToQuery()}");
}

/// <summary>Projetos, equipe, repositórios, dashboards e atividades do projeto.</summary>
public sealed class ProjectsApi(GestalynApi api)
{
    public Task<ApiResult<Paged<ProjectSummary>>> ListAsync(Guid organizationId, bool includeArchived, string? tag, Priority? priority, int page) =>
        api.GetAsync<Paged<ProjectSummary>>(
            $"organizations/{organizationId}/projects{Query.Of(("includeArchived", includeArchived), ("tag", tag), ("priority", priority), ("page", page), ("pageSize", 25))}");

    public Task<ApiResult<ProjectDetails>> CreateAsync(Guid organizationId, object body) =>
        api.SendAsync<ProjectDetails>(HttpMethod.Post, $"organizations/{organizationId}/projects", body);

    public Task<ApiResult<ProjectDetails>> GetAsync(Guid organizationId, Guid projectId) =>
        api.GetAsync<ProjectDetails>($"projects/{projectId}", organizationId);

    public Task<ApiResult<ProjectDetails>> UpdateAsync(Guid organizationId, Guid projectId, object body) =>
        api.SendAsync<ProjectDetails>(HttpMethod.Patch, $"projects/{projectId}", body, organizationId);

    public Task<ApiResult> ArchiveAsync(Guid organizationId, Guid projectId) =>
        api.SendAsync(HttpMethod.Delete, $"projects/{projectId}", organizationId: organizationId);

    public Task<ApiResult<List<ProjectMemberDetails>>> ListMembersAsync(Guid organizationId, Guid projectId) =>
        api.GetAsync<List<ProjectMemberDetails>>($"projects/{projectId}/members", organizationId);

    public Task<ApiResult> AddMemberAsync(Guid organizationId, Guid projectId, Guid userId, ProjectRole role, bool isResponsible) =>
        api.SendAsync(HttpMethod.Post, $"projects/{projectId}/members", new { userId, role, isResponsible }, organizationId);

    public Task<ApiResult> UpdateMemberAsync(Guid organizationId, Guid projectId, Guid userId, ProjectRole? role, bool? isResponsible) =>
        api.SendAsync(HttpMethod.Patch, $"projects/{projectId}/members/{userId}", new { role, isResponsible }, organizationId);

    public Task<ApiResult> RemoveMemberAsync(Guid organizationId, Guid projectId, Guid userId) =>
        api.SendAsync(HttpMethod.Delete, $"projects/{projectId}/members/{userId}", organizationId: organizationId);

    public Task<ApiResult<List<ProjectRepositoryDetails>>> ListRepositoriesAsync(Guid organizationId, Guid projectId) =>
        api.GetAsync<List<ProjectRepositoryDetails>>($"projects/{projectId}/repositories", organizationId);

    public Task<ApiResult> AddRepositoryAsync(Guid organizationId, Guid projectId, string url, string? name) =>
        api.SendAsync(HttpMethod.Post, $"projects/{projectId}/repositories", new { url, name }, organizationId);

    public Task<ApiResult> RemoveRepositoryAsync(Guid organizationId, Guid projectId, Guid repositoryId) =>
        api.SendAsync(HttpMethod.Delete, $"projects/{projectId}/repositories/{repositoryId}", organizationId: organizationId);

    public Task<ApiResult<ProjectDashboard>> DashboardAsync(Guid organizationId, Guid projectId) =>
        api.GetAsync<ProjectDashboard>($"projects/{projectId}/dashboard", organizationId);

    public Task<ApiResult<ProjectProgress>> ProgressAsync(Guid organizationId, Guid projectId) =>
        api.GetAsync<ProjectProgress>($"projects/{projectId}/progress", organizationId);

    public Task<ApiResult<ProjectStatistics>> StatisticsAsync(Guid organizationId, Guid projectId, int weeks) =>
        api.GetAsync<ProjectStatistics>($"projects/{projectId}/statistics?weeks={weeks}", organizationId);

    public Task<ApiResult<Paged<ActivityItem>>> ActivitiesAsync(Guid organizationId, Guid projectId, ActivityFilter filter) =>
        api.GetAsync<Paged<ActivityItem>>($"projects/{projectId}/activities{filter.ToQuery()}", organizationId);

    public Task<ApiResult> LogActivityAsync(Guid organizationId, Guid projectId, object body) =>
        api.SendAsync(HttpMethod.Post, $"projects/{projectId}/activities", body, organizationId);
}

/// <summary>Fases e marcos.</summary>
public sealed class PlanningApi(GestalynApi api)
{
    public Task<ApiResult<List<PhaseDetails>>> ListPhasesAsync(Guid organizationId, Guid projectId) =>
        api.GetAsync<List<PhaseDetails>>($"projects/{projectId}/phases", organizationId);

    public Task<ApiResult> CreatePhaseAsync(Guid organizationId, Guid projectId, object body) =>
        api.SendAsync(HttpMethod.Post, $"projects/{projectId}/phases", body, organizationId);

    public Task<ApiResult> UpdatePhaseAsync(Guid organizationId, Guid projectId, Guid phaseId, object body) =>
        api.SendAsync(HttpMethod.Patch, $"projects/{projectId}/phases/{phaseId}", body, organizationId);

    public Task<ApiResult> ReorderPhasesAsync(Guid organizationId, Guid projectId, IReadOnlyList<Guid> phaseIds) =>
        api.SendAsync(HttpMethod.Put, $"projects/{projectId}/phases/order", new { phaseIds }, organizationId);

    public Task<ApiResult> DeletePhaseAsync(Guid organizationId, Guid projectId, Guid phaseId) =>
        api.SendAsync(HttpMethod.Delete, $"projects/{projectId}/phases/{phaseId}", organizationId: organizationId);

    public Task<ApiResult<List<MilestoneDetails>>> ListMilestonesAsync(Guid organizationId, Guid projectId) =>
        api.GetAsync<List<MilestoneDetails>>($"projects/{projectId}/milestones", organizationId);

    public Task<ApiResult> CreateMilestoneAsync(Guid organizationId, Guid projectId, object body) =>
        api.SendAsync(HttpMethod.Post, $"projects/{projectId}/milestones", body, organizationId);

    public Task<ApiResult> UpdateMilestoneAsync(Guid organizationId, Guid projectId, Guid milestoneId, object body) =>
        api.SendAsync(HttpMethod.Patch, $"projects/{projectId}/milestones/{milestoneId}", body, organizationId);

    public Task<ApiResult> DeleteMilestoneAsync(Guid organizationId, Guid projectId, Guid milestoneId) =>
        api.SendAsync(HttpMethod.Delete, $"projects/{projectId}/milestones/{milestoneId}", organizationId: organizationId);
}

/// <summary>Filtros da lista de tarefas, iguais aos da API.</summary>
public sealed record TaskFilter(
    string? Search = null,
    Guid? StateId = null,
    StateCategory? Category = null,
    Guid? PhaseId = null,
    Guid? MilestoneId = null,
    Guid? AssigneeId = null,
    bool AssignedToMe = false,
    string? Label = null,
    Priority? Priority = null,
    bool Overdue = false,
    bool Open = false,
    bool IncludeArchived = false,
    TaskSortField SortBy = TaskSortField.Number,
    SortDirection SortDirection = SortDirection.Asc,
    int Page = 1,
    int PageSize = 25)
{
    public string ToQuery() => Query.Of(
        ("search", Search),
        ("stateId", StateId),
        ("category", Category),
        ("phaseId", PhaseId),
        ("milestoneId", MilestoneId),
        ("assigneeId", AssigneeId),
        ("assignedToMe", AssignedToMe),
        ("label", Label),
        ("priority", Priority),
        ("overdue", Overdue),
        ("open", Open),
        ("includeArchived", IncludeArchived),
        ("sortBy", SortBy == TaskSortField.Number ? null : SortBy),
        ("sortDirection", SortDirection == SortDirection.Asc ? null : SortDirection),
        ("page", Page == 1 ? null : Page),
        ("pageSize", PageSize));
}

/// <summary>Filtros da lista de atividades, iguais aos da API.</summary>
public sealed record ActivityFilter(
    ActivitySource? Source = null,
    string? Action = null,
    ActivityOrigin? Origin = null,
    Guid? TaskId = null,
    Guid? ProjectId = null,
    DateOnly? From = null,
    DateOnly? To = null,
    int Page = 1,
    int PageSize = 50)
{
    public string ToQuery() => Query.Of(
        ("source", Source),
        ("action", Action),
        ("origin", Origin),
        ("taskId", TaskId),
        ("projectId", ProjectId),
        ("from", From is { } from ? new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero) : null),
        ("to", To is { } to ? new DateTimeOffset(to.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero) : null),
        ("page", Page == 1 ? null : Page),
        ("pageSize", PageSize));
}

/// <summary>Tarefas: dados, estado, responsáveis, dependências, comentários, anexos e histórico.</summary>
public sealed class TasksApi(GestalynApi api)
{
    public Task<ApiResult<Paged<TaskSummary>>> ListAsync(Guid organizationId, Guid projectId, TaskFilter filter) =>
        api.GetAsync<Paged<TaskSummary>>($"projects/{projectId}/tasks{filter.ToQuery()}", organizationId);

    public Task<ApiResult<TaskDetails>> CreateAsync(Guid organizationId, Guid projectId, object body) =>
        api.SendAsync<TaskDetails>(HttpMethod.Post, $"projects/{projectId}/tasks", body, organizationId);

    public Task<ApiResult<TaskDetails>> GetAsync(Guid organizationId, Guid taskId) => api.GetAsync<TaskDetails>($"tasks/{taskId}", organizationId);

    public Task<ApiResult> UpdateAsync(Guid organizationId, Guid taskId, object body) =>
        api.SendAsync(HttpMethod.Patch, $"tasks/{taskId}", body, organizationId);

    public Task<ApiResult> ArchiveAsync(Guid organizationId, Guid taskId) =>
        api.SendAsync(HttpMethod.Delete, $"tasks/{taskId}", organizationId: organizationId);

    public Task<ApiResult> TransitionAsync(Guid organizationId, Guid taskId, Guid toStateId, Guid? expectedStateId, string? comment) =>
        api.SendAsync(HttpMethod.Post, $"tasks/{taskId}/transitions", new { toStateId, expectedStateId, comment }, organizationId);

    public Task<ApiResult> AssignAsync(Guid organizationId, Guid taskId, Guid userId) =>
        api.SendAsync(HttpMethod.Post, $"tasks/{taskId}/assignments", new { userId }, organizationId);

    public Task<ApiResult> UnassignAsync(Guid organizationId, Guid taskId, Guid userId) =>
        api.SendAsync(HttpMethod.Delete, $"tasks/{taskId}/assignments/{userId}", organizationId: organizationId);

    public Task<ApiResult> AddDependencyAsync(Guid organizationId, Guid taskId, Guid dependsOnTaskId) =>
        api.SendAsync(HttpMethod.Post, $"tasks/{taskId}/dependencies", new { dependsOnTaskId }, organizationId);

    public Task<ApiResult> RemoveDependencyAsync(Guid organizationId, Guid taskId, Guid dependsOnTaskId) =>
        api.SendAsync(HttpMethod.Delete, $"tasks/{taskId}/dependencies/{dependsOnTaskId}", organizationId: organizationId);

    public Task<ApiResult<List<CommentDetails>>> ListCommentsAsync(Guid organizationId, Guid taskId) =>
        api.GetAsync<List<CommentDetails>>($"tasks/{taskId}/comments", organizationId);

    public Task<ApiResult> AddCommentAsync(Guid organizationId, Guid taskId, string body) =>
        api.SendAsync(HttpMethod.Post, $"tasks/{taskId}/comments", new { body }, organizationId);

    public Task<ApiResult> EditCommentAsync(Guid organizationId, Guid taskId, Guid commentId, string body) =>
        api.SendAsync(HttpMethod.Patch, $"tasks/{taskId}/comments/{commentId}", new { body }, organizationId);

    public Task<ApiResult> DeleteCommentAsync(Guid organizationId, Guid taskId, Guid commentId) =>
        api.SendAsync(HttpMethod.Delete, $"tasks/{taskId}/comments/{commentId}", organizationId: organizationId);

    public Task<ApiResult<List<AttachmentDetails>>> ListAttachmentsAsync(Guid organizationId, Guid taskId) =>
        api.GetAsync<List<AttachmentDetails>>($"tasks/{taskId}/attachments", organizationId);

    public async Task<ApiResult> UploadAsync(Guid organizationId, Guid taskId, Stream content, string fileName)
    {
        using var form = new MultipartFormDataContent();
        var file = new StreamContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "file", fileName);
        return await api.SendAsync(HttpMethod.Post, $"tasks/{taskId}/attachments", form, organizationId);
    }

    public Task<ApiResult> DeleteAttachmentAsync(Guid organizationId, Guid taskId, Guid attachmentId) =>
        api.SendAsync(HttpMethod.Delete, $"tasks/{taskId}/attachments/{attachmentId}", organizationId: organizationId);

    public Task<HttpResponseMessage?> OpenAttachmentAsync(Guid organizationId, Guid taskId, Guid attachmentId) =>
        api.OpenAsync($"tasks/{taskId}/attachments/{attachmentId}/content", organizationId);

    public Task<ApiResult<List<TaskHistoryEntry>>> HistoryAsync(Guid organizationId, Guid taskId) =>
        api.GetAsync<List<TaskHistoryEntry>>($"tasks/{taskId}/history", organizationId);
}

/// <summary>Configuração do workflow.</summary>
public sealed class WorkflowApi(GestalynApi api)
{
    public Task<ApiResult<WorkflowDetails>> GetAsync(Guid organizationId, Guid projectId) =>
        api.GetAsync<WorkflowDetails>($"projects/{projectId}/workflow", organizationId);

    public Task<ApiResult> UpdateSettingsAsync(Guid organizationId, Guid projectId, Guid? initialStateId, bool? requireIndependentApproval) =>
        api.SendAsync(HttpMethod.Put, $"projects/{projectId}/workflow", new { initialStateId, requireIndependentApproval }, organizationId);

    public Task<ApiResult> AddStateAsync(Guid organizationId, Guid projectId, string name, StateCategory category) =>
        api.SendAsync(HttpMethod.Post, $"projects/{projectId}/workflow/states", new { name, category }, organizationId);

    public Task<ApiResult> UpdateStateAsync(Guid organizationId, Guid stateId, string? name, StateCategory? category) =>
        api.SendAsync(HttpMethod.Patch, $"workflow/states/{stateId}", new { name, category }, organizationId);

    public Task<ApiResult> MoveStateAsync(Guid organizationId, Guid stateId, int position) =>
        api.SendAsync(HttpMethod.Post, $"workflow/states/{stateId}/move", new { position }, organizationId);

    public Task<ApiResult> DeleteStateAsync(Guid organizationId, Guid stateId) =>
        api.SendAsync(HttpMethod.Delete, $"workflow/states/{stateId}", organizationId: organizationId);

    public Task<ApiResult> AddTransitionAsync(Guid organizationId, Guid fromStateId, Guid toStateId, bool requiresApproval) =>
        api.SendAsync(HttpMethod.Post, "workflow/transitions", new { fromStateId, toStateId, requiresApproval }, organizationId);

    public Task<ApiResult> RemoveTransitionAsync(Guid organizationId, Guid transitionId) =>
        api.SendAsync(HttpMethod.Delete, $"workflow/transitions/{transitionId}", organizationId: organizationId);
}

/// <summary>Documentos e decisões.</summary>
public sealed class DocumentsApi(GestalynApi api)
{
    public Task<ApiResult<List<DocumentSummary>>> ListAsync(Guid organizationId, Guid projectId, bool includeArchived) =>
        api.GetAsync<List<DocumentSummary>>($"projects/{projectId}/documents{Query.Of(("includeArchived", includeArchived))}", organizationId);

    public Task<ApiResult<DocumentDetails>> CreateAsync(Guid organizationId, Guid projectId, string title, string body) =>
        api.SendAsync<DocumentDetails>(HttpMethod.Post, $"projects/{projectId}/documents", new { title, body }, organizationId);

    public Task<ApiResult<DocumentDetails>> GetAsync(Guid organizationId, Guid documentId) =>
        api.GetAsync<DocumentDetails>($"documents/{documentId}", organizationId);

    public Task<ApiResult> UpdateAsync(Guid organizationId, Guid documentId, string title, string body, int expectedVersion) =>
        api.SendAsync(HttpMethod.Put, $"documents/{documentId}", new { title, body, expectedVersion }, organizationId);

    public Task<ApiResult<List<DocumentVersionSummary>>> VersionsAsync(Guid organizationId, Guid documentId) =>
        api.GetAsync<List<DocumentVersionSummary>>($"documents/{documentId}/versions", organizationId);

    public Task<ApiResult<DocumentVersionDetails>> VersionAsync(Guid organizationId, Guid documentId, int number) =>
        api.GetAsync<DocumentVersionDetails>($"documents/{documentId}/versions/{number}", organizationId);

    public Task<ApiResult> ArchiveAsync(Guid organizationId, Guid documentId) =>
        api.SendAsync(HttpMethod.Post, $"documents/{documentId}/archive", organizationId: organizationId);

    public Task<ApiResult> RestoreAsync(Guid organizationId, Guid documentId) =>
        api.SendAsync(HttpMethod.Post, $"documents/{documentId}/restore", organizationId: organizationId);

    public Task<ApiResult<List<DecisionSummary>>> ListDecisionsAsync(Guid organizationId, Guid projectId, DecisionStatus? status) =>
        api.GetAsync<List<DecisionSummary>>($"projects/{projectId}/decisions{Query.Of(("status", status))}", organizationId);

    public Task<ApiResult<DecisionDetails>> ProposeAsync(Guid organizationId, Guid projectId, object body) =>
        api.SendAsync<DecisionDetails>(HttpMethod.Post, $"projects/{projectId}/decisions", body, organizationId);

    public Task<ApiResult<DecisionDetails>> GetDecisionAsync(Guid organizationId, Guid decisionId) =>
        api.GetAsync<DecisionDetails>($"decisions/{decisionId}", organizationId);

    public Task<ApiResult> UpdateDecisionAsync(Guid organizationId, Guid decisionId, object body) =>
        api.SendAsync(HttpMethod.Patch, $"decisions/{decisionId}", body, organizationId);

    public Task<ApiResult> DecideAsync(Guid organizationId, Guid decisionId, string verb) =>
        api.SendAsync(HttpMethod.Post, $"decisions/{decisionId}/{verb}", organizationId: organizationId);

    public Task<ApiResult> SupersedeAsync(Guid organizationId, Guid decisionId, Guid supersededById) =>
        api.SendAsync(HttpMethod.Post, $"decisions/{decisionId}/supersede", new { supersededById }, organizationId);
}
