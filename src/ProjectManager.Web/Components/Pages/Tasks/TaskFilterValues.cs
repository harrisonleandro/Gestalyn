using ProjectManager.Web.Api;

namespace ProjectManager.Web.Components.Pages.Tasks;

/// <summary>Filtros de tarefas como vêm da URL (nomes em pt-BR), convertidos para os filtros da API.</summary>
public sealed record TaskFilterValues(
    string? Search,
    Guid? StateId,
    string? Category,
    string? Priority,
    string? Label,
    bool Mine,
    bool Overdue,
    bool Open,
    bool Archived,
    string? Sort)
{
    public TaskFilter ToFilter(int? page, int pageSize = 25)
    {
        var sortBy = Enum.TryParse<TaskSortField>(Sort, out var field) ? field : TaskSortField.Number;
        var direction = sortBy is TaskSortField.Priority or TaskSortField.UpdatedAt ? SortDirection.Desc : SortDirection.Asc;
        return new TaskFilter(
            Search: string.IsNullOrWhiteSpace(Search) ? null : Search.Trim(),
            StateId: StateId,
            Category: Enum.TryParse<StateCategory>(Category, out var category) ? category : null,
            Priority: Enum.TryParse<Api.Priority>(Priority, out var priority) ? priority : null,
            Label: string.IsNullOrWhiteSpace(Label) ? null : Label.Trim(),
            AssignedToMe: Mine,
            Overdue: Overdue,
            Open: Open,
            IncludeArchived: Archived,
            SortBy: sortBy,
            SortDirection: direction,
            Page: Math.Max(page ?? 1, 1),
            PageSize: pageSize);
    }
}
