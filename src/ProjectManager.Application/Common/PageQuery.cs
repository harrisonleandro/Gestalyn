using System.ComponentModel.DataAnnotations;

namespace ProjectManager.Application.Common;

/// <summary>
/// Paginação por página e tamanho, comum às listagens. Uma página além do fim responde com a lista
/// vazia e o total correto.
/// </summary>
public abstract class PageQuery
{
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 100;

    /// <summary>
    /// Maior página cujo deslocamento, <c>(Page - 1) * MaxPageSize</c>, ainda cabe num <c>int</c>
    /// (21.474.837 com 100 itens por página). O limite é derivado do tipo usado pelo <c>Skip</c> do EF
    /// Core, não escolhido à mão, e acompanha <see cref="MaxPageSize"/> se ele mudar.
    /// </summary>
    public const int MaxPage = (int.MaxValue / MaxPageSize) + 1;

    [Range(1, MaxPage, ErrorMessage = "A página deve ficar entre {1} e {2}.")]
    public int Page { get; init; } = 1;

    [Range(1, MaxPageSize, ErrorMessage = "O tamanho da página deve ficar entre {1} e {2}.")]
    public int PageSize { get; init; } = DefaultPageSize;

    /// <summary>
    /// Itens a pular. Com a validação, nunca passa de <c>int.MaxValue</c>; sem ela (uso interno
    /// indevido), a conta verificada falha em vez de gerar um deslocamento negativo. É um método, e não
    /// uma propriedade, porque a validação do ASP.NET Core lê as propriedades do modelo antes de
    /// recusá-lo: uma propriedade lançaria a exceção e transformaria o 400 em 500.
    /// </summary>
    public int CalculateOffset() => checked((Page - 1) * PageSize);
}
