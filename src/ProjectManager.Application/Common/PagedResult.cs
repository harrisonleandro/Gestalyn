namespace ProjectManager.Application.Common;

/// <summary>Página de uma listagem, com o total de itens que atendem aos filtros.</summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);
