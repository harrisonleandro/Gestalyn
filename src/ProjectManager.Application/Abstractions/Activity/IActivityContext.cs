using ProjectManager.Domain.Activity;

namespace ProjectManager.Application.Abstractions.Activity;

/// <summary>
/// Autor e origem da operação em curso (ADR-014). Na API, o autor vem do <c>sub</c> do token e a origem do
/// cabeçalho <c>X-Gestalyn-Client</c>; fora de uma requisição, a operação é do sistema.
/// </summary>
public interface IActivityContext
{
    ActivityActor Actor { get; }
}
