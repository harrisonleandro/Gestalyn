using ProjectManager.Domain.Workflows;

namespace ProjectManager.Application.Common;

/// <summary>Quem fez algo: uma pessoa ou um agente (ADR-016), com o nome para exibição.</summary>
public sealed record ActorReference(Guid Id, ActorKind Kind, string? Name);
